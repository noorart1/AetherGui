using AetherGui;

static class Checks
{
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }

    [STAThread]
    static void Main()
    {
        var options = CoreOptions.Parse("[\"--tor-bridge\",\"obfs4 1.2.3.4:443 cert=abc iat-mode=0\"]",
            "{\"AETHER_SCAN\":\"balanced\"}", 600);
        Check(options.Arguments.Length == 2 && options.Arguments[1].Contains(" iat-mode=0"), "Arguments preserve spaces");
        Check(options.StartupSeconds == 600, "Advanced timeout");
        foreach (var value in new[] { "[\"--bind=0.0.0.0:80\"]", "[\"--tor\"]", "[null]", "[1]", "{}" })
        {
            bool rejected = false;
            try { CoreOptions.Parse(value, "{}", 300); }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { rejected = true; }
            Check(rejected, "Reject invalid arguments " + value);
        }
        foreach (var value in new[] { "{\"AETHER_SOCKS\":\"x\"}", "{\"PATH\":\"x\"}", "{\"AETHER_IP\":null}" })
        {
            bool rejected = false;
            try { CoreOptions.Parse("[]", value, 300); }
            catch (FormatException) { rejected = true; }
            Check(rejected, "Reject invalid environment " + value);
        }
        foreach (var preset in Aether.Presets)
        {
            var info = Aether.CreateStartInfo(preset, options);
            var args = info.ArgumentList.ToList();
            string Value(string flag) => args[args.IndexOf(flag) + 1];
            bool tor = preset.Args.Split(' ').Contains("--tor"), psiphon = preset.Args.Split(' ').Contains("--psiphon");
            Check(Value("--bind") == (tor || psiphon ? "127.0.0.1:1822" : "127.0.0.1:1818"), preset.Name + " base listener");
            Check(Value(preset.HttpFlag) == "127.0.0.1:1819", preset.Name + " HTTP exit");
            if (tor) Check(Value("--tor-bind") == "127.0.0.1:1818", "Tor SOCKS exit");
            if (psiphon) Check(Value("--psiphon-bind") == "127.0.0.1:1818", "Psiphon SOCKS exit");
            Check(info.Environment["AETHER_SCAN"] == "balanced" && !args.Contains("--turbo"), "Environment overrides defaults");
            Check(args.Contains(options.Arguments[1]) && !info.UseShellExecute, "No shell splitting");
        }
        Check(Aether.CreateStartInfo(Aether.Presets[0], new CoreOptions(), "balanced").Environment["AETHER_SCAN"] == "balanced", "Chosen scan mode reaches the core");
        Check(Aether.CreateStartInfo(Aether.Presets[0], new CoreOptions()).Environment["AETHER_SCAN"] == "turbo", "Fast scan is the default");
        Check(Aether.ParseVersion("v2.4.0") == new Version(2, 4, 0) && Aether.ParseVersion("aether 2.3.0\n") == new Version(2, 3, 0), "Release tag and core output parse");
        Check(Aether.ParseVersion("1.0.0.0") == Aether.ParseVersion("v1.0.0") && Aether.ParseVersion("nightly") is null, "Version compare ignores the fourth part");
        Check(new Aether.Updates(new Version(2, 3, 0), new Version(2, 4, 0), null, null).CoreNewer
            && !new Aether.Updates(new Version(2, 4, 0), new Version(2, 4, 0), Aether.AppVersion, null).CoreNewer
            && !new Aether.Updates(null, null, Aether.AppVersion, null).GuiNewer, "Only a newer release counts as an update");
        Check(Aether.Presets[4].Name == "Tor alone" && Aether.Presets[7].Name == "Psiphon + WARP", "Saved indexes stable");
        Check(Aether.AutoPresets.Any(p => p.Args.Contains("--h2")), "Auto HTTP/2 fallback");
        Check(Aether.MenuPresetIndexes.Order().SequenceEqual(Enumerable.Range(0, Aether.Presets.Length)), "Menu includes every stable preset index exactly once");
        Check(Aether.MenuPresetIndexes.Take(Aether.AutoPresets.Length).All(i => !Aether.Presets[i].ManualOnly), "Fast protocols appear first");
        Check(Aether.MenuPresetIndexes.Skip(Aether.AutoPresets.Length).All(i => Aether.Presets[i].ManualOnly), "Manual protocols appear last");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var help = Aether.ReadHelpAsync(timeout.Token).GetAwaiter().GetResult();
        Check(help.Contains("--tor") && help.Contains("--psiphon"), "Real bundled core help executes");
        Check(help.Contains("--gool-classic"), "Bundled core supports v2.3 classic gool");
        Check(Aether.Presets[2].Args.Contains("--gool-classic"), "Saved gool keeps WireGuard-in-WireGuard semantics");
        Check(Aether.Presets[21].Args.Split(' ').Contains("--gool"), "New gool appended without shifting saved indexes");
        bool classicRejected = false;
        try { CoreOptions.Parse("[\"--gool-classic\"]", "{}", 300); }
        catch (FormatException) { classicRejected = true; }
        Check(classicRejected, "Classic gool cannot override selected protocol");
        ApplicationConfiguration.Initialize();
        using var settings = new CoreSettingsForm(new());
        foreach (var size in new[] { new Size(520, 460), new Size(800, 650), new Size(1200, 800) })
        {
            settings.Size = size; settings.PerformLayout();
            Check(settings.Controls[0].Width > 0 && settings.Controls[0].Height > 0, "Settings layout " + size);
        }
        Console.WriteLine("All core checks passed. No tunnel or system proxy was started.");
    }
}
