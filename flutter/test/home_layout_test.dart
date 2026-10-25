import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:aether_gui/aether.dart';
import 'package:aether_gui/main.dart';

void main() {
  for (final size in [
    const Size(320, 568),
    const Size(844, 390),
    const Size(1200, 800),
  ]) {
    testWidgets('log stays visible and settings open at $size', (tester) async {
      final dir = Directory.systemTemp.createTempSync('aether-layout-');
      Aether.dir = dir.path;
      Aether.exe = '${dir.path}/missing-core';
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = size;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
        dir.deleteSync(recursive: true);
      });
      await tester.pumpWidget(
        MaterialApp(theme: ThemeData.dark(), home: const HomePage()),
      );
      await tester.pumpAndSettle();
      final menu = tester.widget<DropdownMenu<int>>(
        find.byType(DropdownMenu<int>),
      );
      final entries = menu.dropdownMenuEntries;
      expect(entries.first.value, -1);
      final expectedIndexes = [
        for (final manualOnly in [false, true])
          for (final (i, p) in Aether.presets.indexed)
            if (p.manualOnly == manualOnly) i,
      ];
      expect(entries.skip(1).map((e) => e.value), expectedIndexes);
      for (final entry in entries.skip(1)) {
        expect(entry.label, Aether.presets[entry.value].label);
      }
      final log = find.text('Waiting for logs...');
      expect(log, findsOneWidget);
      expect(tester.getRect(log).bottom, lessThan(size.height));
      expect(find.text('Show log'), findsNothing);
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('Core settings'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('Additional arguments (JSON array)'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('Additional arguments (JSON array)'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
    });
  }
}
