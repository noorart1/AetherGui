import 'package:flutter_test/flutter_test.dart';
import 'package:aether_gui/aether.dart';

void main() {
  test('release tags and app versions parse', () {
    expect(Aether.parseVersion('v1.2.0'), [1, 2, 0]);
    expect(Aether.parseVersion('1.0.0+1'), [1, 0, 0]);
    expect(Aether.parseVersion('v2.4'), [2, 4, 0]);
    expect(Aether.parseVersion('nightly'), isNull);
  });

  test('only a newer release counts as an update', () {
    expect(Aether.isNewer([1, 0, 1], [1, 0, 0]), isTrue);
    expect(Aether.isNewer([1, 10, 0], [1, 9, 9]), isTrue);
    expect(Aether.isNewer([1, 0, 0], [1, 0, 0]), isFalse);
    expect(Aether.isNewer([0, 9, 0], [1, 0, 0]), isFalse);
  });
}
