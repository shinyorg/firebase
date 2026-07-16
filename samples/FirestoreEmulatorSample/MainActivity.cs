using Android.App;
using Android.OS;
using Android.Util;
using Android.Widget;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Firestore.Mobile;
using FB = Com.Google.Firebase;

namespace FsTestApp;

[Activity(Label = "FsTest", MainLauncher = true)]
public class MainActivity : Activity
{
    const string TAG = "SHINYFS";

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var tv = new TextView(this);
        SetContentView(tv);

        try
        {
            // Init Firebase with throwaway options — the Firestore emulator accepts any demo project.
            var fbOptions = new FB.FirebaseOptions.Builder()
                .SetProjectId("demo-shiny")
                .SetApplicationId("1:1234567890:android:abcdef123456")
                .SetApiKey("fake-api-key")
                .Build();
            FB.FirebaseApp.InitializeApp(this, fbOptions);
            Log.Info(TAG, "RESULT firebase-init: OK");

            var opts = new MobileFirestoreOptions
            {
                ProjectId = "demo-shiny",
                EmulatorHost = "10.0.2.2:8080", // host loopback from the Android emulator
                PersistenceEnabled = false
            };
            var store = new MobileFirestoreDocumentStore(opts, null!);
            Log.Info(TAG, "RESULT store-ctor: OK");

            await store.Insert(new Play { Id = "p1", Name = "Slant Left", Version = 1 });
            Log.Info(TAG, "RESULT insert: OK");

            var got = await store.Get<Play>("p1");
            var ok1 = got is { Name: "Slant Left" };
            Log.Info(TAG, $"RESULT get: {(ok1 ? "PASS" : "FAIL")} name={got?.Name ?? "<null>"}");

            await store.Upsert(new Play { Id = "p1", Name = "Slant Right", Version = 2 });
            var got2 = await store.Get<Play>("p1");
            var ok2 = got2 is { Name: "Slant Right", Version: 2 };
            Log.Info(TAG, $"RESULT upsert: {(ok2 ? "PASS" : "FAIL")} name={got2?.Name ?? "<null>"}");

            await store.Remove<Play>("p1");
            var gone = await store.Get<Play>("p1");
            var ok3 = gone == null;
            Log.Info(TAG, $"RESULT remove: {(ok3 ? "PASS" : "FAIL")}");

            // Real-time: subscribe, then write and confirm the listener fires with the change.
            var changes = new System.Collections.Generic.List<DocumentChange<Play>>();
            var sub = await ((IChangeFeedDocumentStore)store).SubscribeChanges<Play>(
                (c, ct) => { lock (changes) changes.Add(c); return Task.CompletedTask; });
            await Task.Delay(1500); // let the initial (skipped) snapshot settle
            await store.Insert(new Play { Id = "rt1", Name = "Realtime Play", Version = 1 });
            await Task.Delay(2500); // let the snapshot listener deliver
            bool ok4;
            lock (changes)
                ok4 = changes.Exists(c => c.Id == "rt1" && c.ChangeType == DocumentChangeType.Inserted
                                          && c.Document?.Name == "Realtime Play");
            Log.Info(TAG, $"RESULT realtime: {(ok4 ? "PASS" : "FAIL")} changes={changes.Count}");
            await sub.DisposeAsync();
            await store.Remove<Play>("rt1");

            // Query: filter + order + count + executeDelete.
            await store.Insert(new Play { Id = "q1", Name = "Alpha", Version = 1 });
            await store.Insert(new Play { Id = "q2", Name = "Charlie", Version = 3 });
            await store.Insert(new Play { Id = "q3", Name = "Bravo", Version = 2 });

            // Inequality filter → first OrderBy must be on the inequality field (Firestore rule).
            var filtered = await store.Query<Play>().Where(p => p.Version >= 2).OrderBy(p => p.Version).ToList();
            var ok5 = filtered.Count == 2 && filtered[0].Name == "Bravo" && filtered[1].Name == "Charlie";
            Log.Info(TAG, $"RESULT query-where-order: {(ok5 ? "PASS" : "FAIL")} [{string.Join(",", filtered.Select(f => f.Name))}]");

            var total = await store.Query<Play>().Count();
            var ok6 = total == 3;
            Log.Info(TAG, $"RESULT query-count: {(ok6 ? "PASS" : "FAIL")} count={total}");

            var deleted = await store.Query<Play>().Where(p => p.Version >= 2).ExecuteDelete();
            var remaining = await store.Query<Play>().Count();
            var ok7 = deleted == 2 && remaining == 1;
            Log.Info(TAG, $"RESULT query-delete: {(ok7 ? "PASS" : "FAIL")} deleted={deleted} remaining={remaining}");

            await store.Query<Play>().ExecuteDelete(); // cleanup

            // Identity: managed Firebase Auth REST against the Auth emulator.
            IFirebaseIdentity identity = new FirebaseRestIdentity(new FirebaseIdentityOptions
            {
                ApiKey = "fake-api-key",           // emulator accepts any key
                AuthEmulatorHost = "10.0.2.2:9099"
            });
            var anon = await identity.SignInAnonymouslyAsync();
            var token = await identity.GetIdTokenAsync();
            var ok8 = !string.IsNullOrEmpty(anon.Uid) && anon.IsAnonymous
                      && !string.IsNullOrEmpty(token) && identity.CurrentUserId == anon.Uid;
            Log.Info(TAG, $"RESULT identity-anon: {(ok8 ? "PASS" : "FAIL")} uid={anon.Uid}");

            var email = $"tester{anon.Uid[..6]}@example.com";
            var signedUp = await identity.SignUpWithEmailPasswordAsync(email, "Passw0rd!");
            var signedIn = await identity.SignInWithEmailPasswordAsync(email, "Passw0rd!");
            var ok9 = signedUp.Uid == signedIn.Uid && signedIn.Email == email && !signedIn.IsAnonymous;
            Log.Info(TAG, $"RESULT identity-email: {(ok9 ? "PASS" : "FAIL")} uid={signedIn.Uid}");
            identity.SignOut();

            var overall = ok1 && ok2 && ok3 && ok4 && ok5 && ok6 && ok7 && ok8 && ok9 ? "ALLPASS" : "SOMEFAIL";
            Log.Info(TAG, $"RESULT overall: {overall}");
            tv.Text = overall;
        }
        catch (Exception ex)
        {
            Log.Error(TAG, "RESULT overall: EXCEPTION " + ex);
            tv.Text = "EXCEPTION: " + ex.Message;
        }
    }
}

public class Play
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; }
}
