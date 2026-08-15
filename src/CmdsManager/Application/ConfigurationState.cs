using System;
using CmdsManager.Domain;

namespace CmdsManager.Application
{
    public sealed class ConfigurationState
    {
        private AppConfiguration _current;

        public ConfigurationState(AppConfiguration configuration)
        {
            _current = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public AppConfiguration Current
        {
            get => _current;
            set
            {
                _current = value ?? throw new ArgumentNullException(nameof(value));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler Changed;
    }
}
