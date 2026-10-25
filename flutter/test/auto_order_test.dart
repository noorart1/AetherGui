import 'package:flutter_test/flutter_test.dart';
import 'package:aether_gui/aether.dart';

void main() {
  int index(String name) => Aether.presets.indexWhere((p) => p.name == name);

  test('Auto tries the fastest past connect first and failed ones last', () {
    final past = {
      index('WireGuard'): 9000,
      index('MASQUE'): 3000,
      index('MASQUE-in-MASQUE'): -1,
    };
    for (var run = 0; run < 20; run++) {
      final order = Aether.autoOrder(past).map((p) => p.name).toList();
      expect(order.take(2), ['MASQUE', 'WireGuard']);
      expect(order.last, 'MASQUE-in-MASQUE');
      expect(order.length, Aether.presets.where((p) => !p.manualOnly).length);
    }
  });

  test('connect times round-trip and junk is dropped', () {
    final past = {index('MASQUE'): 3000, index('MASQUE-in-MASQUE'): -1};
    final saved = Aether.parseConnectMs(
      '${Aether.formatConnectMs(past)},999:5,x:1',
    );
    expect(saved, past);
  });
}
