import 'dart:io';
import 'dart:math';

import 'package:flutter/material.dart';

import 'aether.dart';
import 'core_options.dart';
import 'core_settings_page.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Aether.init();
  runApp(
    MaterialApp(
      title: 'Aether VPN',
      debugShowCheckedModeBanner: false,
      // Platform defaults: Material light/dark following the system setting.
      theme: ThemeData(),
      darkTheme: ThemeData(brightness: Brightness.dark),
      home: const HomePage(),
    ),
  );
}

// Status colors only; everything else uses the default theme.
abstract final class Palette {
  static const connected = Colors.green;
  static const connecting = Colors.orange;
  static const disconnected = Colors.red;
}

class HomePage extends StatefulWidget {
  const HomePage({super.key});
  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  static const startupDelayMs =
      3000; // pause before the automatic connect on startup
  static const red = Palette.disconnected,
      orange = Palette.connecting,
      seaGreen = Palette.connected;

  AetherProcess? _process;
  Cancel? _ct;
  bool _autoConnect = false;
  CoreOptions _coreOptions = const CoreOptions();
  int _preset = -1; // index into Aether.presets, -1 = Auto (rotate through all)
  String _scan = 'turbo'; // AETHER_SCAN: turbo or balanced
  String _status = 'Disconnected';
  Color _statusColor = red;
  String _delay = 'Delay: -';
  final _lines = <(String, Color?)>[];
  final _scroll = ScrollController();
  final _rng = Random();

  File get _settings => File('${Aether.dir}/settings.txt');

  @override
  void initState() {
    super.initState();
    try {
      final s = _settings.readAsStringSync();
      _autoConnect = s.contains('AutoConnect=1');
      final i =
          int.tryParse(
            RegExp(r'Preset=(-?\d+)').firstMatch(s)?.group(1) ?? '',
          ) ??
          -1;
      _preset = i >= -1 && i < Aether.presets.length ? i : -1;
      if (s.contains('Scan=balanced')) _scan = 'balanced';
    } catch (_) {}
    try {
      _coreOptions = CoreOptions.load(Aether.dir);
    } catch (_) {
      _autoConnect = false;
      _log('Could not load core settings. Review settings before connecting.');
    }
    _checkUpdates(interactive: false);
    if (_autoConnect) {
      _log('Auto-connecting in ${startupDelayMs ~/ 1000}s...');
      Future.delayed(const Duration(milliseconds: startupDelayMs), () {
        if (mounted && _autoConnect && _ct == null) _connect();
      });
    }
  }

  @override
  void dispose() {
    _ct?.cancel();
    _killProcess();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _connect() async {
    if (_ct != null) return _ct!.cancel();
    final ct = _ct = Cancel();
    setState(() {});
    await _run(ct);
    _ct = null;
    if (mounted) setState(() {});
  }

  void _saveSettings() => _settings.writeAsStringSync(
    'AutoConnect=${_autoConnect ? 1 : 0}\nPreset=$_preset\nScan=$_scan\n',
  );

  // Auto: try the fast presets in random order; one that times out or drops is not retried until all others were tried.
  // A chosen preset (including the slow manual-only ones) is simply retried.
  Future<void> _run(Cancel ct) async {
    final untried = <Preset>[];
    try {
      if (!File(Aether.exe).existsSync())
        return _log('aether binary missing: run fetch_aether.sh and rebuild.');
      while (true) {
        if (untried.isEmpty) {
          untried.addAll(
            _preset < 0
                ? Aether.presets.where((p) => !p.manualOnly)
                : [Aether.presets[_preset]],
          );
        }
        await _connectAndMonitor(
          untried.removeAt(_rng.nextInt(untried.length)),
          ct,
        );
        await ct.delay(1000);
      }
    } on Cancelled {
      // user pressed Disconnect
    } finally {
      Aether.vpnStop();
      _killProcess();
      _setStatus('Disconnected', red);
      _setDelay('Delay: -');
    }
  }

  // Returns when the preset fails to connect within the timeout, or after a connected session drops.
  Future<void> _connectAndMonitor(Preset p, Cancel ct) async {
    _log('Trying ${p.name}...');
    _setStatus('Connecting: ${p.name}', orange);
    final proc = _process = await Aether.start(p, _log, _coreOptions, _scan);
    if (proc == null) return;

    int? ms = await Aether.waitConnected(
      proc,
      _coreOptions.isConfigured ? _coreOptions.startupSeconds : p.startupSecs,
      ct,
    );
    if (ms == null) {
      _log(
        proc.exited
            ? '${p.name}: process exited.'
            : '${p.name}: no internet, switching.',
      );
      _killProcess();
      return;
    }

    _setStatus('Connected: ${p.name}', seaGreen);
    _log('Connected via ${p.name}, delay $ms ms.');
    _log(
      await Aether.vpnStart()
          ? 'VPN on: all apps go through aether.'
          : 'VPN permission denied: proxy only.',
    );
    for (var fails = 0; fails < 2;) {
      _setDelay(ms == null ? 'Delay: timeout' : 'Delay: $ms ms');
      await ct.delay(5000);
      if (proc.exited) break;
      ms = await Aether.ping(ct);
      fails = ms == null ? fails + 1 : 0;
    }
    _log('${p.name}: connection lost, switching.');
    _killProcess();
  }

  void _killProcess() {
    _process?.p.kill(ProcessSignal.sigkill);
    _process = null;
  }

  Future<void> _showCoreSettings() async {
    final options = await Navigator.push<CoreOptions>(
      context,
      MaterialPageRoute(
        builder: (_) => CoreSettingsPage(options: _coreOptions),
      ),
    );
    if (mounted && options != null) setState(() => _coreOptions = options);
  }

  Future<void> _sendCoreInput() async {
    final proc = _process;
    if (proc == null || proc.exited) return;
    final input = TextEditingController();
    final value = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Core input'),
        content: TextField(
          controller: input,
          autofocus: true,
          obscureText: true,
          autocorrect: false,
          enableSuggestions: false,
          decoration: const InputDecoration(
            labelText: 'Code or prompt response',
          ),
          onSubmitted: (value) => Navigator.pop(context, value),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, input.text),
            child: const Text('Send'),
          ),
        ],
      ),
    );
    // Let the closing dialog finish before disposing its controller.
    await Future<void>.delayed(const Duration(milliseconds: 300));
    input.dispose();
    if (!mounted || value == null || value.isEmpty) return;
    if (_process != proc || proc.exited) {
      _log('Core changed while input was open. Input was not sent.');
      return;
    }
    try {
      proc.p.stdin.writeln(value);
      await proc.p.stdin.flush();
      _log('Input sent to core.');
    } catch (_) {
      _log('Could not send input to core.');
    }
  }

  // The startup check only logs a new release; the button also offers to open its page.
  Future<void> _checkUpdates({required bool interactive}) async {
    if (interactive) _log('Checking for updates...');
    try {
      final have = await Aether.appVersion();
      final latest = await Aether.latestGuiRelease();
      final mine = Aether.parseVersion(have),
          theirs = Aether.parseVersion(latest?.$1);
      if (!mounted) return;
      if (latest == null ||
          mine == null ||
          theirs == null ||
          !Aether.isNewer(theirs, mine)) {
        if (interactive) _log('Up to date: Aether $have.');
        return;
      }
      _log('Aether ${latest.$1} is available (you have $have).', Colors.blue);
      if (!interactive || !latest.$2.startsWith('https://github.com/')) return;
      final open = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Update available'),
          content: Text(
            'Aether ${latest.$1} is available. Open the download page?',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Later'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Open'),
            ),
          ],
        ),
      );
      if (open == true) await Aether.openUrl(latest.$2);
    } catch (e) {
      if (interactive) _log('Update check failed: $e');
    }
  }

  void _showProxyInfo() {
    const head = Colors.blue, info = null, tip = Colors.orange;
    final secrets = Directory(Aether.dir)
        .listSync()
        .where((f) => RegExp(r'[/\\]aether[^/\\]*\.toml$').hasMatch(f.path))
        .length;
    _log('=== Proxy info ===', head);
    _log(
      'HTTP 127.0.0.1:${Aether.httpPort} | SOCKS5 127.0.0.1:${Aether.socksPort}',
      info,
    );
    _log(
      'aether: ${_process != null && !_process!.exited ? 'running' : 'stopped'} | $_status | $_delay',
      info,
    );
    _log('Presets: ${Aether.presets.map((p) => p.name).join(', ')}', info);
    _log(
      'Cleanup: Disconnect, or Android stops aether together with the app.',
      info,
    );
    _log(
      'Credits: aether by CluvexStudio (github.com/CluvexStudio) | VPN via hev-socks5-tunnel (heiher)',
      info,
    );
    _log('=== Recommendations ===', head);
    _log(
      '1. Allow the VPN prompt: it routes browsers/most apps through aether on any network.',
      tip,
    );
    _log(
      '2. $secrets aether*.toml file(s) in app storage hold private keys/tokens: rotate keys if the device is shared.',
      tip,
    );
    _log(
      '3. Auto only rotates the fast protocols (30s each). Tor/Psiphon are manual: pick them on strict networks.',
      tip,
    );
  }

  void _setStatus(String text, Color color) {
    if (mounted)
      setState(() {
        _status = text;
        _statusColor = color;
      });
  }

  void _setDelay(String text) {
    if (mounted) setState(() => _delay = text);
  }

  void _log(String msg, [Color? color]) {
    if (!mounted) return;
    final t = DateTime.now().toIso8601String().substring(11, 19);
    setState(() => _lines.add(('[$t] $msg', color)));
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) _scroll.jumpTo(_scroll.position.maxScrollExtent);
    });
  }

  // Color is never the only signal: each state also has its own icon and text.
  IconData get _statusIcon => switch (_statusColor) {
    seaGreen => Icons.check_circle,
    orange => Icons.autorenew,
    _ => Icons.cancel,
  };

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    final busy = _ct != null;
    final connecting = busy && _statusColor == orange;
    final reduceMotion = MediaQuery.disableAnimationsOf(context);

    final controls = Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                const Icon(Icons.shield_outlined),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Secure connection',
                        style: text.titleMedium?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      Text(
                        busy
                            ? 'Tap the power button to disconnect'
                            : 'Choose a route, then connect',
                        style: text.bodySmall,
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: _connect,
              icon: const Icon(Icons.power_settings_new),
              label: Text(busy ? 'Disconnect' : 'Connect'),
            ),
            // progress bar while a protocol is being tried; static under reduced motion
            if (connecting)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: LinearProgressIndicator(value: reduceMotion ? 1 : null),
              ),
            const SizedBox(height: 16),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                ExcludeSemantics(
                  child: Icon(_statusIcon, color: _statusColor, size: 22),
                ),
                const SizedBox(width: 8),
                Flexible(
                  child: Text(
                    _status,
                    style: text.titleLarge?.copyWith(color: _statusColor),
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text(_delay, textAlign: TextAlign.center, style: text.bodyMedium),
            const SizedBox(height: 28),
            DropdownMenu<int>(
              label: const Text('Protocol'),
              leadingIcon: const Icon(Icons.vpn_lock),
              expandedInsets: EdgeInsets.zero,
              enabled: !busy, // pick before connecting; Disconnect to change
              initialSelection: _preset,
              dropdownMenuEntries: [
                const DropdownMenuEntry(
                  value: -1,
                  label: 'Auto (fast protocols)',
                ),
                // Keep persisted values stable while displaying fast presets first.
                for (final manualOnly in [false, true])
                  for (final (i, p) in Aether.presets.indexed)
                    if (p.manualOnly == manualOnly)
                      DropdownMenuEntry(value: i, label: p.label),
              ],
              onSelected: (v) {
                setState(() => _preset = v ?? -1);
                _saveSettings();
              },
            ),
            const SizedBox(height: 16),
            Text('Server search', style: text.titleSmall),
            const SizedBox(height: 8),
            SegmentedButton<String>(
              segments: const [
                ButtonSegment(
                  value: 'turbo',
                  label: Text('Fast'),
                  tooltip: 'First server that answers',
                ),
                ButtonSegment(
                  value: 'balanced',
                  label: Text('Balanced'),
                  tooltip: 'Tries a few servers, keeps the fastest',
                ),
              ],
              selected: {_scan},
              onSelectionChanged: busy
                  ? null
                  : (v) {
                      setState(() => _scan = v.first);
                      _saveSettings();
                    },
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              secondary: const Icon(Icons.bolt_outlined),
              title: const Text('Connect on startup'),
              subtitle: const Text(
                'Start the VPN automatically when Aether opens',
              ),
              value: _autoConnect,
              onChanged: (v) {
                setState(() => _autoConnect = v);
                _saveSettings();
              },
            ),
          ],
        ),
      ),
    );

    return Scaffold(
      appBar: AppBar(
        title: const Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Aether'),
            Text('Private network client', style: TextStyle(fontSize: 12)),
          ],
        ),
        actions: [
          IconButton(
            tooltip: 'Core settings',
            icon: const Icon(Icons.tune),
            onPressed: busy ? null : _showCoreSettings,
          ),
          IconButton(
            tooltip: 'Core input',
            icon: const Icon(Icons.keyboard),
            onPressed: busy ? _sendCoreInput : null,
          ),
          IconButton(
            tooltip: 'Proxy info',
            icon: const Icon(Icons.info_outline),
            onPressed: _showProxyInfo,
          ),
          IconButton(
            tooltip: 'Check for updates',
            icon: const Icon(Icons.system_update),
            onPressed: () => _checkUpdates(interactive: true),
          ),
        ],
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
          child: LayoutBuilder(
            builder: (context, constraints) {
              // Keep the log in the viewport, even on short screens or with
              // large text. Only the controls scroll when they do not fit.
              final controlPane = SingleChildScrollView(
                padding: const EdgeInsets.only(bottom: 8),
                child: controls,
              );
              final logPane = Card(
                clipBehavior: Clip.antiAlias,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Padding(
                      padding: const EdgeInsets.fromLTRB(16, 12, 8, 8),
                      child: Row(
                        children: [
                          const Icon(Icons.terminal, size: 20),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              'Activity',
                              style: text.titleSmall?.copyWith(
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                          IconButton(
                            tooltip: 'Clear activity',
                            onPressed: _lines.isEmpty
                                ? null
                                : () => setState(_lines.clear),
                            icon: const Icon(Icons.delete_outline),
                          ),
                        ],
                      ),
                    ),
                    const Divider(height: 1),
                    Expanded(
                      child: SelectionArea(
                        child: ListView.builder(
                          controller: _scroll,
                          padding: const EdgeInsets.all(16),
                          itemCount: _lines.isEmpty ? 1 : _lines.length,
                          itemBuilder: (_, i) => Padding(
                            padding: const EdgeInsets.only(bottom: 4),
                            child: Text(
                              _lines.isEmpty
                                  ? 'Waiting for logs...'
                                  : _lines[i].$1,
                              style: TextStyle(
                                color: _lines.isEmpty ? null : _lines[i].$2,
                                fontFamily: 'monospace',
                                fontSize: 12,
                                height: 1.45,
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              );

              if (constraints.maxWidth >= 600) {
                return Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SizedBox(
                      width: min(360.0, constraints.maxWidth * 0.45),
                      child: controlPane,
                    ),
                    const SizedBox(width: 16),
                    Expanded(child: logPane),
                  ],
                );
              }

              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ConstrainedBox(
                    constraints: BoxConstraints(
                      maxHeight: constraints.maxHeight * 0.62,
                    ),
                    child: controlPane,
                  ),
                  const SizedBox(height: 12),
                  Expanded(child: logPane),
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}
