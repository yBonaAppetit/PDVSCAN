namespace PdvBarcodeFilter;

internal static class BuiltInSelfTest
{
    internal static bool Run()
    {
        try
        {
            var settings = new FilterSettings();
            var processor = new ScanProcessor(settings);
            const string first = "11111111-2222-3333-4444-555555555555|STD01|AUX01";

            if (processor.Process(first).Output != "STD01" ||
                processor.Process(first).Output != "AUX01" ||
                processor.Process(first).Output != "AUX01" ||
                processor.Process("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|AB123|ZZ999").Output != "AB123" ||
                processor.Process("bbbbbbbb-cccc-dddd-eeee-ffffffffffff}CD456}YY888").Output != "CD456" ||
                processor.Process("qualquertexto").IsValid)
            {
                return false;
            }

            var accumulator = new ScanAccumulator(settings.MaxScanLength);
            foreach (var character in first)
            {
                accumulator.Append(character.ToString());
            }

            var captured = accumulator.Complete();
            var floodGuard = new ScannerFloodGuard(settings);
            var floodAllowed = true;
            for (var index = 0; index < settings.MaxScansPerFloodWindow; index++)
            {
                floodAllowed &= floodGuard.Evaluate(1000 + index) == FloodDecision.Allowed;
            }

            var floodBlocked = floodGuard.Evaluate(1011) == FloodDecision.JustBlocked;
            var hotkey = HotkeyGesture.Parse(settings.ToggleHotkey);

            return !captured.Overflowed &&
                   captured.RawCode == first &&
                   processor.HasValidStructure(first) &&
                   !processor.HasValidStructure("entrada comum") &&
                   floodAllowed &&
                   floodBlocked &&
                   hotkey.VirtualKey == (uint)Keys.F12 &&
                   hotkey.DisplayText == "Ctrl+Shift+F12" &&
                   KeyboardSender.NativeInputStructureSize == KeyboardSender.ExpectedNativeInputStructureSize &&
                   KeyboardCapturePolicy.ShouldCapture(0) &&
                   !KeyboardCapturePolicy.ShouldCapture(KeyboardCapturePolicy.LlkhfInjected);
        }
        catch
        {
            return false;
        }
    }
}
