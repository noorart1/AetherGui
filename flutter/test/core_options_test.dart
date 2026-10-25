import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:aether_gui/aether.dart';
import 'package:aether_gui/core_options.dart';

void main() {
  setUp(() {
    Aether.lyrebird = '/native/liblyrebird.so';
    Aether.psiphon = '/native/libpsiphon.so';
  });

  test('preserves bridge arguments containing spaces', () {
    final options = CoreOptions.parse(
      '["--tor-bridge", "obfs4 1.2.3.4:443 fingerprint cert=abc iat-mode=0"]',
      '{"AETHER_ROUTE_SNIFF_MS":"600"}',
      '600',
    );
    expect(options.arguments.length, 2);
    expect(options.arguments[1], contains(' iat-mode=0'));
    expect(options.environment['AETHER_ROUTE_SNIFF_MS'], '600');
  });

  test('rejects invalid data and managed overrides', () {
    for (final args in [
      '{}',
      '[1]',
      '["--bind=0.0.0.0:80"]',
      '["--tor"]',
      '["--register"]',
      '["--gool-classic"]',
      '["--gool-classic=true"]',
    ]) {
      expect(() => CoreOptions.parse(args, '{}', '300'), throwsFormatException);
    }
    for (final env in [
      '[]',
      '{"HOME":"/tmp"}',
      '{"AETHER_SOCKS":"x"}',
      '{"AETHER_IP":4}',
    ]) {
      expect(() => CoreOptions.parse('[]', env, '300'), throwsFormatException);
    }
    expect(() => CoreOptions.parse('[]', '{}', '0'), throwsFormatException);
    expect(() => CoreOptions.parse('[]', '{}', '3601'), throwsFormatException);
  });

  test('v2.3 preserves classic gool and appends the new MASQUE carriers', () {
    for (final index in [2, 14, 19]) {
      final args = Aether.launchArguments(
        Aether.presets[index],
        const CoreOptions(),
      );
      expect(args, contains('--gool-classic'));
      expect(args, isNot(contains('--gool')));
    }
    expect(Aether.presets[21].name, 'WireGuard-in-MASQUE (gool)');
    expect(Aether.presets[22].name, 'WireGuard-in-MASQUE HTTP/2');
    for (final index in [21, 22]) {
      final preset = Aether.presets[index];
      final args = Aether.launchArguments(preset, const CoreOptions());
      expect(args, contains('--gool'));
      expect(args, isNot(contains('--gool-classic')));
      expect(args.contains('--h2'), index == 22);
      expect(preset.manualOnly, isFalse);
    }
  });

  test('settings survive save/load', () async {
    final dir = Directory.systemTemp.createTempSync('aether-settings-');
    try {
      final options = CoreOptions.parse(
        '["--stats"]',
        '{"AETHER_IP":"dual"}',
        '450',
      );
      await options.save(dir.path);
      final loaded = CoreOptions.load(dir.path);
      expect(loaded.arguments, options.arguments);
      expect(loaded.environment, options.environment);
      expect(loaded.startupSeconds, 450);
    } finally {
      dir.deleteSync(recursive: true);
    }
  });

  test(
    'all presets use the correct VPN and HTTP exit without port collisions',
    () {
      for (final preset in Aether.presets) {
        final args = Aether.launchArguments(preset, const CoreOptions());
        String value(String flag) => args[args.indexOf(flag) + 1];
        final tor = preset.args.split(' ').contains('--tor');
        final psiphon = preset.args.split(' ').contains('--psiphon');
        expect(value(preset.httpFlag), '127.0.0.1:1819', reason: preset.name);
        expect(
          value('--bind'),
          tor || psiphon ? '127.0.0.1:1822' : '127.0.0.1:1818',
          reason: preset.name,
        );
        if (tor) expect(value('--tor-bind'), '127.0.0.1:1818');
        if (psiphon) expect(value('--psiphon-bind'), '127.0.0.1:1818');
        expect(args, isNot(contains('--turbo')));
        expect(args, isNot(contains('-4')));
      }
    },
  );

  test('existing saved indexes stay stable and Auto includes h2 fallback', () {
    expect(Aether.presets[4].name, 'Tor alone');
    expect(Aether.presets[7].name, 'Psiphon + WARP');
    expect(
      Aether.presets.where((p) => !p.manualOnly && p.args.contains('--h2')),
      isNotEmpty,
    );
    expect(
      Aether.presets
          .where((p) => p.args.contains('--tor'))
          .every((p) => p.manualOnly),
      isTrue,
    );
  });
}
