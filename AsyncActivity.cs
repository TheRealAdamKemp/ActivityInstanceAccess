using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace ActivityInstanceAccess
{
    [Activity (Label = "AsyncActivity")]
    public class AsyncActivity : FragmentActivity<AsyncActivityFragment>
    {
        public const string TextExtra = "Text";
    }

    public class AsyncActivityFragment : FragmentBase
    {
        private EditText _editText;

        public string Text { get { return _editText?.Text; } }

        public event EventHandler DoneClicked;

        private void OnDoneClicked()
        {
            if (DoneClicked != null)
            {
                DoneClicked(this, EventArgs.Empty);
            }
        }

        public override View OnCreateView (LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = inflater.Inflate(Resource.Layout.AsyncActivity, container, attachToRoot: false);

            var button = view.FindViewById<Button>(Resource.Id.Button);
            _editText = view.FindViewById<EditText>(Resource.Id.EditText);

            button.Click += delegate
            {
                OnDoneClicked();
                var resultData = new Intent();
                resultData.PutExtra(AsyncActivity.TextExtra, _editText.Text);
                Activity.SetResult(Result.Ok, resultData);
                Activity.Finish();
            };

            return view;
        }
    }
}

