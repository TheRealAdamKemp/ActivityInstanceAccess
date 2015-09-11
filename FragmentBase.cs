using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;

namespace ActivityInstanceAccess
{
    /// <summary>
    /// The information returned by an Activity started with StartActivityForResultAsync.
    /// </summary>
    public interface IAsyncActivityResult
    {
        /// <summary>
        /// The result code returned by the activity.
        /// </summary>
        Result ResultCode { get; }

        /// <summary>
        /// The data returned by the activity.
        /// </summary>
        Intent Data { get; }
    }

    /// <summary>
    /// The base class for top-level fragments in Android. These are the fragments which maintain the view hierarchy and state for each top-level
    /// Activity. These fragments all use RetainInstance = true to allow them to maintain state across configuration changes (i.e.,
    /// when the device rotates we reuse the fragments). Activity classes are basically just dumb containers for these fragments.
    /// </summary>
    public abstract class FragmentBase : Fragment, Application.IActivityLifecycleCallbacks
    {
        // This is an arbitrary number to use as an initial request code for StartActivityForResultAsync.
        // It just needs to be high enough to avoid collisions with direct calls to StartActivityForResult, which typically would be 0, 1, 2...
        private const int FirstAsyncActivityRequestCode = 1000;

        private const string AsyncActivityRequestCodeExtra = "AsyncActivityRequestCodeExtra";

        // This is static so that they are unique across all implementations of FragmentBase.
        // This is important for the fragment initializer overloads of StartActivityForResultAsync.
        private static int _nextAsyncActivityRequestCode = FirstAsyncActivityRequestCode;
        private readonly Dictionary<int, AsyncActivityResult> _pendingAsyncActivities = new Dictionary<int, AsyncActivityResult>();
        private readonly List<AsyncActivityResult> _finishedAsyncActivityResults = new List<AsyncActivityResult>();

        private int _numberOfPendingFragmentInitializers;

        /// <summary>
        /// Tries to locate an already created fragment with the given tag. If the fragment is not found then a new one will be created and inserted into
        /// the given activity using the given containerId as the parent view.
        /// </summary>
        /// <typeparam name="TFragment">The type of fragment to create.</typeparam>
        /// <param name="activity">The activity to search for or create the view in.</param>
        /// <param name="fragmentTag">The tag which uniquely identifies the fragment.</param>
        /// <param name="containerId">The resource ID of the parent view to use for a newly created fragment.</param>
        /// <param name = "fragment">The fragment that was found or created.</param>
        /// <returns>true if the fragment was newly created.</returns>
        public static bool FindOrCreateFragment<TFragment>(Activity activity, string fragmentTag, int containerId, out TFragment fragment) where TFragment : FragmentBase, new()
        {
            fragment = activity.FragmentManager.FindFragmentByTag(fragmentTag) as TFragment;
            if (fragment == null)
            {
                fragment = new TFragment();
                activity.FragmentManager.BeginTransaction().Add(containerId, fragment, fragmentTag).Commit();

                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            RetainInstance = true;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (_numberOfPendingFragmentInitializers != 0)
            {
                var application = (Application)Application.Context;
                application.UnregisterActivityLifecycleCallbacks(this);
                _numberOfPendingFragmentInitializers = 0;
            }
        }

        /// <summary>
        /// Called when this fragment's activity is given a new Intent.
        /// </summary>
        /// <remarks>The default implementation does nothing</remarks>
        public virtual void OnNewIntent(Intent intent)
        {
        }

        /// <summary>
        /// Called when this fragment's activity is attached to a window.
        /// </summary>
        /// <remarks>The default implementation does nothing</remarks>
        public virtual void OnAttachedToWindow()
        {
        }

        #region Async Activity API

        public Task<IAsyncActivityResult> StartActivityForResultAsync<TActivity>(CancellationToken cancellationToken = default(CancellationToken))
        {
            return StartActivityForResultAsyncCore(requestCode => Activity.StartActivityForResult(typeof(TActivity), requestCode), cancellationToken);
        }

        public Task<IAsyncActivityResult> StartActivityForResultAsync<TFragmentActivity, TFragment>(Action<TFragment> fragmentInitializer, CancellationToken cancellationToken = default(CancellationToken))
            where TFragmentActivity : IFragmentActivity
            where TFragment : Fragment
        {
            Action<Fragment> fragmentInitializerAdaptor = null;
            if (fragmentInitializer != null)
            {
                fragmentInitializerAdaptor = fragment => fragmentInitializer((TFragment)fragment);
            }

            return StartActivityForResultAsyncCore(
                requestCode =>
                {
                    AddPendingFragmentInitializer();
                    var intent = new Intent(Activity, typeof(TFragmentActivity));
                    intent.PutExtra(AsyncActivityRequestCodeExtra, requestCode);
                    Activity.StartActivityForResult(intent, requestCode);
                },
                cancellationToken,
                fragmentInitializerAdaptor);
        }

        public Task<IAsyncActivityResult> StartActivityForResultAsync(Intent intent, CancellationToken cancellationToken = default(CancellationToken))
        {
            return StartActivityForResultAsyncCore(requestCode => Activity.StartActivityForResult(intent, requestCode), cancellationToken);
        }

        public override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            AsyncActivityResult result;
            if (_pendingAsyncActivities.TryGetValue(requestCode, out result))
            {
                result.SetResult(resultCode, data);
                _pendingAsyncActivities.Remove(requestCode);
                _finishedAsyncActivityResults.Add(result);
            }

            base.OnActivityResult(requestCode, resultCode, data);
        }

        public override void OnResume()
        {
            base.OnResume();

            FlushPendingAsyncActivityResults();
        }

        private Task<IAsyncActivityResult> StartActivityForResultAsyncCore(Action<int> startActivity, CancellationToken cancellationToken, Action<Fragment> fragmentInitializer = null)
        {
            var asyncActivityResult = SetupAsyncActivity(fragmentInitializer);
            startActivity(asyncActivityResult.RequestCode);

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                    {
                        Activity.FinishActivity(asyncActivityResult.RequestCode);
                    });
            }

            return asyncActivityResult.Task;
        }

        private void FlushPendingAsyncActivityResults()
        {
            foreach (var result in _finishedAsyncActivityResults)
            {
                result.Complete();
            }
            _finishedAsyncActivityResults.Clear();
        }

        private AsyncActivityResult SetupAsyncActivity(Action<Fragment> fragmentInitializer)
        {
            var requestCode = _nextAsyncActivityRequestCode++;
            var result = new AsyncActivityResult(requestCode, fragmentInitializer);
            _pendingAsyncActivities.Add(requestCode, result);

            return result;
        }

        private class AsyncActivityResult : IAsyncActivityResult
        {
            private readonly TaskCompletionSource<IAsyncActivityResult> _taskCompletionSource = new TaskCompletionSource<IAsyncActivityResult>();

            public int RequestCode { get; private set; }

            public Result ResultCode { get; private set; }

            public Intent Data { get; private set; }

            public Task<IAsyncActivityResult> Task { get { return _taskCompletionSource.Task; } }

            public Action<Fragment> FragmentInitializer { get; private set; }

            public AsyncActivityResult(int requestCode, Action<Fragment> fragmentInitializer)
            {
                RequestCode = requestCode;
                FragmentInitializer = fragmentInitializer;
            }

            public void SetResult(Result resultCode, Intent data)
            {
                ResultCode = resultCode;
                Data = data;
            }

            public void Complete()
            {
                _taskCompletionSource.SetResult(this);
            }
        }

        #endregion

        private void AddPendingFragmentInitializer()
        {
            _numberOfPendingFragmentInitializers++;
            if (_numberOfPendingFragmentInitializers == 1)
            {
                var application = (Application)Application.Context;
                application.RegisterActivityLifecycleCallbacks(this);
            }
        }

        private void RemovePendingFragmentInitializer()
        {
            if (_numberOfPendingFragmentInitializers <= 0)
            {
                throw new InvalidOperationException("Too many calls to RemovePendingFragmentInitializer");
            }

            _numberOfPendingFragmentInitializers--;
            if (_numberOfPendingFragmentInitializers == 0)
            {
                var application = (Application)Application.Context;
                application.UnregisterActivityLifecycleCallbacks(this);
            }
        }

        #region IActivityLifecycleCallbacks implementation

        void Application.IActivityLifecycleCallbacks.OnActivityCreated(Activity activity, Bundle savedInstanceState)
        {
            var fragmentActivity = activity as IFragmentActivity;
            if (fragmentActivity == null)
            {
                return;
            }

            var intent = activity.Intent;
            if (intent != null && intent.HasExtra(AsyncActivityRequestCodeExtra))
            {
                int requestCode = intent.GetIntExtra(AsyncActivityRequestCodeExtra, 0);
                AsyncActivityResult asyncActivityResult;
                if (_pendingAsyncActivities.TryGetValue(requestCode, out asyncActivityResult))
                {
                    if (asyncActivityResult.FragmentInitializer == null)
                    {
                        return;
                    }

                    fragmentActivity.FragmentLoaded += (s, e) =>
                    {
                        asyncActivityResult.FragmentInitializer(fragmentActivity.Fragment);

                        // It is possible that this activity is created and destroyed multiple times before loading the fragment.
                        // Don't stop listening for new activities until we actually get the fragment we are interested in.
                        RemovePendingFragmentInitializer();
                    };
                }
            }
        }

        void Application.IActivityLifecycleCallbacks.OnActivityDestroyed(Activity activity)
        {
        }

        void Application.IActivityLifecycleCallbacks.OnActivityPaused(Activity activity)
        {
        }

        void Application.IActivityLifecycleCallbacks.OnActivityResumed(Activity activity)
        {
        }

        void Application.IActivityLifecycleCallbacks.OnActivitySaveInstanceState(Activity activity, Bundle outState)
        {
        }

        void Application.IActivityLifecycleCallbacks.OnActivityStarted(Activity activity)
        {
        }

        void Application.IActivityLifecycleCallbacks.OnActivityStopped(Activity activity)
        {
        }
        #endregion
    }
}