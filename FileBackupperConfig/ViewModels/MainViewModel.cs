using FileBackupper.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileBackupperConfig.ViewModels
{
    class MainViewModel : INotifyPropertyChanged
    {
        private StartInfo _model;

        #region Properties

        public event PropertyChangedEventHandler PropertyChanged;

        public int Limit
        {
            get { return (int)_model.Limit.TotalDays; }
            set
            {
                _model.Limit = TimeSpan.FromDays(value);
                PropertyChanged?.Invoke(this, _LimitChangedEventArgs);
            }
        }
        private PropertyChangedEventArgs _LimitChangedEventArgs = new PropertyChangedEventArgs(nameof(Limit));

        public string VaultPath
        {
            get { return _model.VaultPath; }
            set
            {
                if (_model.VaultPath == value) return;
                _model.VaultPath = value;
                PropertyChanged?.Invoke(this, _VaultPathChangedEventArgs);
            }
        }
        private PropertyChangedEventArgs _VaultPathChangedEventArgs = new PropertyChangedEventArgs(nameof(VaultPath));

        #endregion
    }
}
