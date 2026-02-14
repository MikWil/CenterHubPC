using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class UtilitiesViewModel : BaseViewModel
    {
        private MoveFilesViewModel? _moveFilesViewModel;
        private ComputerViewModel? _computerViewModel;
        private JsonStringifyViewModel? _jsonStringifyViewModel;
        private ConverterToolsViewModel? _converterToolsViewModel;

        public MoveFilesViewModel MoveFilesViewModel
        {
            get
            {
                _moveFilesViewModel ??= App.Services.GetService(typeof(MoveFilesViewModel)) as MoveFilesViewModel;
                return _moveFilesViewModel!;
            }
        }

        public ComputerViewModel ComputerViewModel
        {
            get
            {
                _computerViewModel ??= App.Services.GetService(typeof(ComputerViewModel)) as ComputerViewModel;
                return _computerViewModel!;
            }
        }

        public JsonStringifyViewModel JsonStringifyViewModel
        {
            get
            {
                _jsonStringifyViewModel ??= App.Services.GetService(typeof(JsonStringifyViewModel)) as JsonStringifyViewModel;
                return _jsonStringifyViewModel!;
            }
        }

        public ConverterToolsViewModel ConverterToolsViewModel
        {
            get
            {
                _converterToolsViewModel ??= App.Services.GetService(typeof(ConverterToolsViewModel)) as ConverterToolsViewModel;
                return _converterToolsViewModel!;
            }
        }

        public UtilitiesViewModel(
            ILogger<UtilitiesViewModel>? logger = null) : base(logger)
        {
            Logger?.LogInformation("UtilitiesViewModel initialized");
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Logger?.LogInformation("UtilitiesViewModel disposed");
            }
            base.Dispose(disposing);
        }
    }
}
