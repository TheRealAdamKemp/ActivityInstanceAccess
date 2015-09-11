using System;
using Android.App;
using Android.Content;
using Android.OS;

namespace ActivityInstanceAccess
{
    /// <summary>
    /// An interface for an Activity that uses a retained Fragment for its implementation.
    /// </summary>
    public interface IFragmentActivity
    {
        /// <summary>
        /// The top-level fragment which manages the view and state for this activity.
        /// </summary>
        Fragment Fragment { get; }

        /// <summary>
        /// Invoked when the main fragment is first created.
        /// </summary>
        event EventHandler FragmentLoaded;
    }

    /// <summary>
    /// An Activity that uses a retained Fragment for its implementation.
    /// </summary>
    public abstract class FragmentActivity<TFragment> : Activity, IFragmentActivity where TFragment : FragmentBase, new()
    {
        /// <summary>
        /// The top-level fragment which manages the view and state for this activity.
        /// </summary>
        public FragmentBase Fragment { get; protected set; }

        /// <summary>
        /// The top-level fragment which manages the view and state for this activity.
        /// </summary>
        Fragment IFragmentActivity.Fragment { get { return Fragment; } }

        /// <summary>
        /// Invoked when the main fragment is first created.
        /// </summary>
        public event EventHandler FragmentLoaded;

        /// <summary>
        /// The tag string to use when finding or creating this activity's fragment. This will be contructed using the type of this generic instance.
        /// </summary>
        protected string FragmentTag
        {
            get
            {
                return GetType().Name;
            }
        }

        /// <summary>
        /// Loads the fragment for this activity and stores it in the Fragment property.
        /// </summary>
        protected virtual void LoadFragment()
        {
            TFragment fragment;
            bool createdFragment = FragmentBase.FindOrCreateFragment<TFragment>(this, FragmentTag, Android.Resource.Id.Content, out fragment);

            Fragment = fragment;

            if (createdFragment)
            {
                OnFragmentLoaded();
            }
        }

        /// <inheritdoc />
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            LoadFragment();
        }

        /// <inheritdoc />
        public override void OnAttachedToWindow()
        {
            base.OnAttachedToWindow();
            Fragment.OnAttachedToWindow();
        }

        /// <inheritdoc />
        protected override void OnNewIntent(Intent intent)
        {
            Fragment.OnNewIntent(intent);
        }

        /// <inheritdoc />
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            Fragment.OnActivityResult(requestCode, resultCode, data);
        }

        private void OnFragmentLoaded()
        {
            if (FragmentLoaded != null)
            {
                FragmentLoaded(this, EventArgs.Empty);
            }
        }
    }
}
