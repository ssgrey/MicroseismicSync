using System;
using System.Collections.Generic;

namespace MicroseismicSync.Infrastructure
{
    public sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> registrations = new Dictionary<Type, object>();

        public void RegisterSingleton<TService>(TService instance)
        {
            registrations[typeof(TService)] = instance;
        }

        public TService Resolve<TService>()
        {
            object instance;
            if (!registrations.TryGetValue(typeof(TService), out instance))
            {
                throw new InvalidOperationException("Service not registered: " + typeof(TService).FullName);
            }

            return (TService)instance;
        }
    }
}
