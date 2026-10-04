using System;
using Avalonia;
using Avalonia.Controls;
using CenterHubNew.MVVM.ViewModel;

namespace CenterHubNew.MVVM.View
{
    public partial class DashboardView : UserControl
    {
        private bool _attached;

        public DashboardView()
        {
            InitializeComponent();
        }

        // Only passes "the page is on screen" on to the view-model, which runs the cards' timers while it is.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            (DataContext as DashboardViewModel)?.SetActive(true);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            (DataContext as DashboardViewModel)?.SetActive(false);
            _attached = false;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_attached) (DataContext as DashboardViewModel)?.SetActive(true);
        }
    }
}
