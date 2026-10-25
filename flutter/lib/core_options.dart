import 'dart:convert';
import 'dart:io';

/// Expert settings use JSON arrays, not shell splitting: spaces in bridge lines,
/// routing expressions, tokens and paths must reach the core unchanged.
class CoreOptions {
  final List<String> arguments;
  final Map<String, String> environment;
  final int startupSeconds;

  const CoreOptions({
    this.arguments = const [],
    this.environment = const {},
    this.startupSeconds = 300,
  });

  static const managedFlags = {
    '--bind',
    '--http-proxy',
    '--tor-bind',
    '--tor-http',
    '--psiphon-bind',
    '--psiphon-http',
    '--tor-pt',
    '--tor-pt-dir',
    '--psiphon-bin',
    '--masque',
    '--wg',
    '--wireguard',
    '--warp',
    '--gool',
    '--gool-classic',
    '--wiw',
    '--mim',
    '--masque-in-masque',
    '--protocol',
    '--tor',
    '--tor-only',
    '--tor-reverse',
    '--psiphon',
    '--psiphon-only',
    '--psiphon-reverse',
    '--help',
    'help',
    '--register',
    '--version',
  };
  static const managedEnvironment = {
    'AETHER_SOCKS',
    'AETHER_HTTP_PROXY',
    'AETHER_TOR_BIND',
    'AETHER_TOR_HTTP',
    'AETHER_PSIPHON_BIND',
    'AETHER_PSIPHON_HTTP',
    'AETHER_PROTOCOL',
    'AETHER_TOR',
    'AETHER_PSIPHON',
    'AETHER_TOR_PT',
    'AETHER_TOR_PT_DIR',
    'AETHER_PSIPHON_BIN',
  };

  factory CoreOptions.parse(
    String arguments,
    String environment,
    String timeout,
  ) {
    final args = jsonDecode(arguments);
    final env = jsonDecode(environment);
    final seconds = int.tryParse(timeout);
    if (args is! List || args.any((v) => v is! String)) {
      throw const FormatException('Arguments must be a JSON array of strings.');
    }
    if (env is! Map ||
        env.entries.any((e) => e.key is! String || e.value is! String)) {
      throw const FormatException(
        'Environment must be a JSON object of string values.',
      );
    }
    if (seconds == null || seconds < 30 || seconds > 3600) {
      throw const FormatException('Startup timeout must be 30–3600 seconds.');
    }
    for (final arg in args.cast<String>()) {
      if (managedFlags.contains(arg.split('=').first) ||
          arg.contains('\u0000')) {
        throw FormatException('App-managed or unsupported argument: $arg');
      }
    }
    for (final entry in env.entries) {
      final key = entry.key as String;
      if ((!key.startsWith('AETHER_') && key != 'RUST_LOG') ||
          managedEnvironment.contains(key) ||
          key.contains('=') ||
          key.contains('\u0000') ||
          (entry.value as String).contains('\u0000')) {
        throw FormatException(
          'App-managed or unsupported environment key: $key',
        );
      }
    }
    return CoreOptions(
      arguments: args.cast<String>(),
      environment: env.cast<String, String>(),
      startupSeconds: seconds,
    );
  }

  bool get isConfigured => arguments.isNotEmpty || environment.isNotEmpty;
  String get argumentsJson =>
      const JsonEncoder.withIndent('  ').convert(arguments);
  String get environmentJson =>
      const JsonEncoder.withIndent('  ').convert(environment);

  static CoreOptions load(String directory) {
    final file = File('$directory/core-options.json');
    if (!file.existsSync()) return const CoreOptions();
    final data = jsonDecode(file.readAsStringSync()) as Map<String, dynamic>;
    return CoreOptions.parse(
      jsonEncode(data['arguments']),
      jsonEncode(data['environment']),
      '${data['startupSeconds']}',
    );
  }

  Future<void> save(String directory) => File('$directory/core-options.json')
      .writeAsString(
        jsonEncode({
          'arguments': arguments,
          'environment': environment,
          'startupSeconds': startupSeconds,
        }),
      )
      .then((_) {});
}
