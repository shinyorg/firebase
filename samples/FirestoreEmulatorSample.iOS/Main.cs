using Foundation;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Firestore.Mobile;
using UIKit;

namespace FsTestApp;

public class Application
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    readonly List<string> results = new();

    void Result(string line)
    {
        this.results.Add(line);
        Console.WriteLine("SHINYFS " + line);
        // The file is what the host reads back — stdout from a simulator app is easy to lose.
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            File.WriteAllText(Path.Combine(dir, "results.txt"), string.Join("\n", this.results));
        }
        catch
        {
            // Never let reporting break the run.
        }
    }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        this.Window = new UIWindow(UIScreen.MainScreen.Bounds);
        var vc = new UIViewController();
        var label = new UILabel(this.Window.Bounds) { TextAlignment = UITextAlignment.Center, Text = "running…" };
        vc.View!.AddSubview(label);
        this.Window.RootViewController = vc;
        this.Window.MakeKeyAndVisible();

        _ = Task.Run(async () =>
        {
            var overall = await this.RunAsync();
            this.BeginInvokeOnMainThread(() => label.Text = overall);
        });
        return true;
    }

    async Task<string> RunAsync()
    {
        try
        {
            // The iOS simulator shares the host network stack, so the emulators are on localhost
            // (the Android emulator uses 10.0.2.2 instead).
            var opts = new MobileFirestoreOptions
            {
                ProjectId = "demo-shiny",
                AppId = "1:1234567890:ios:abcdef123456",
                ApiKey = "fake-api-key",
                EmulatorHost = "localhost:8080",
                PersistenceEnabled = false
            };

            var store = new MobileFirestoreDocumentStore(opts, null!);
            this.Result("RESULT store-ctor: OK");

            await store.Insert(new Play { Id = "p1", Name = "Slant Left", Version = 1 });
            this.Result("RESULT insert: OK");

            var got = await store.Get<Play>("p1");
            var ok1 = got is { Name: "Slant Left" };
            this.Result($"RESULT get: {(ok1 ? "PASS" : "FAIL")} name={got?.Name ?? "<null>"}");

            await store.Upsert(new Play { Id = "p1", Name = "Slant Right", Version = 2 });
            var got2 = await store.Get<Play>("p1");
            var ok2 = got2 is { Name: "Slant Right", Version: 2 };
            this.Result($"RESULT upsert: {(ok2 ? "PASS" : "FAIL")} name={got2?.Name ?? "<null>"}");

            await store.Remove<Play>("p1");
            var gone = await store.Get<Play>("p1");
            var ok3 = gone == null;
            this.Result($"RESULT remove: {(ok3 ? "PASS" : "FAIL")}");

            // Real-time: subscribe, then write and confirm the listener fires with the change.
            var changes = new List<DocumentChange<Play>>();
            var sub = await ((IChangeFeedDocumentStore)store).SubscribeChanges<Play>(
                (c, ct) => { lock (changes) changes.Add(c); return Task.CompletedTask; });
            await Task.Delay(1500); // let the initial (skipped) snapshot settle
            await store.Insert(new Play { Id = "rt1", Name = "Realtime Play", Version = 1 });
            await Task.Delay(2500); // let the snapshot listener deliver
            bool ok4;
            lock (changes)
                ok4 = changes.Exists(c => c.Id == "rt1" && c.ChangeType == DocumentChangeType.Inserted
                                          && c.Document?.Name == "Realtime Play");
            this.Result($"RESULT realtime: {(ok4 ? "PASS" : "FAIL")} changes={changes.Count}");
            await sub.DisposeAsync();
            await store.Remove<Play>("rt1");

            // Query: filter + order + count + executeDelete.
            await store.Insert(new Play { Id = "q1", Name = "Alpha", Version = 1 });
            await store.Insert(new Play { Id = "q2", Name = "Charlie", Version = 3 });
            await store.Insert(new Play { Id = "q3", Name = "Bravo", Version = 2 });

            // Inequality filter → first OrderBy must be on the inequality field (Firestore rule).
            var filtered = await store.Query<Play>().Where(p => p.Version >= 2).OrderBy(p => p.Version).ToList();
            var ok5 = filtered.Count == 2 && filtered[0].Name == "Bravo" && filtered[1].Name == "Charlie";
            this.Result($"RESULT query-where-order: {(ok5 ? "PASS" : "FAIL")} [{string.Join(",", filtered.Select(f => f.Name))}]");

            var total = await store.Query<Play>().Count();
            var ok6 = total == 3;
            this.Result($"RESULT query-count: {(ok6 ? "PASS" : "FAIL")} count={total}");

            var deleted = await store.Query<Play>().Where(p => p.Version >= 2).ExecuteDelete();
            var remaining = await store.Query<Play>().Count();
            var ok7 = deleted == 2 && remaining == 1;
            this.Result($"RESULT query-delete: {(ok7 ? "PASS" : "FAIL")} deleted={deleted} remaining={remaining}");

            await store.Query<Play>().ExecuteDelete(); // cleanup

            // Identity: managed Firebase Auth REST against the Auth emulator.
            IFirebaseIdentity identity = new FirebaseRestIdentity(new FirebaseIdentityOptions
            {
                ApiKey = "fake-api-key",          // emulator accepts any key
                AuthEmulatorHost = "localhost:9099"
            });
            var anon = await identity.SignInAnonymouslyAsync();
            var token = await identity.GetIdTokenAsync();
            var ok8 = !string.IsNullOrEmpty(anon.Uid) && anon.IsAnonymous
                      && !string.IsNullOrEmpty(token) && identity.CurrentUserId == anon.Uid;
            this.Result($"RESULT identity-anon: {(ok8 ? "PASS" : "FAIL")} uid={anon.Uid}");

            var email = $"tester{anon.Uid[..6]}@example.com";
            var signedUp = await identity.SignUpWithEmailPasswordAsync(email, "Passw0rd!");
            var signedIn = await identity.SignInWithEmailPasswordAsync(email, "Passw0rd!");
            var ok9 = signedUp.Uid == signedIn.Uid && signedIn.Email == email && !signedIn.IsAnonymous;
            this.Result($"RESULT identity-email: {(ok9 ? "PASS" : "FAIL")} uid={signedIn.Uid}");
            identity.SignOut();

            var overall = ok1 && ok2 && ok3 && ok4 && ok5 && ok6 && ok7 && ok8 && ok9 ? "ALLPASS" : "SOMEFAIL";
            this.Result($"RESULT overall: {overall}");
            return overall;
        }
        catch (Exception ex)
        {
            this.Result("RESULT overall: EXCEPTION " + ex);
            return "EXCEPTION: " + ex.Message;
        }
    }
}

public class Play
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; }
}
