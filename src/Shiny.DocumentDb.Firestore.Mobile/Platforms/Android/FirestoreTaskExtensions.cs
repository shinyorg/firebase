#if ANDROID
using Com.Google.Android.Gms.Tasks;
using NativeTask = Com.Google.Android.Gms.Tasks.Task;
using Object = Java.Lang.Object;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>
/// Awaits a native Play Services <see cref="Com.Google.Android.Gms.Tasks.Task"/> from managed code by
/// attaching success/failure listeners and completing a <see cref="TaskCompletionSource{TResult}"/>. The
/// generic no-arg <c>getResult()</c> is stripped by the binder, so the result is read from the success callback.
/// </summary>
static class FirestoreTaskExtensions
{
    public static Task<Object?> AsAsync(this NativeTask nativeTask)
    {
        var tcs = new TaskCompletionSource<Object?>();
        nativeTask.AddOnSuccessListener(new SuccessListener(tcs));
        nativeTask.AddOnFailureListener(new FailureListener(tcs));
        return tcs.Task;
    }

    public static async System.Threading.Tasks.Task AsVoidAsync(this NativeTask nativeTask)
        => await nativeTask.AsAsync().ConfigureAwait(false);

    sealed class SuccessListener(TaskCompletionSource<Object?> tcs) : Java.Lang.Object, IOnSuccessListener
    {
        public void OnSuccess(Object? result) => tcs.TrySetResult(result);
    }

    sealed class FailureListener(TaskCompletionSource<Object?> tcs) : Java.Lang.Object, IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception e)
            => tcs.TrySetException(new InvalidOperationException(e.Message ?? "Firestore operation failed."));
    }
}
#endif
