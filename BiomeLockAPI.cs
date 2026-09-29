using System;
using System.Collections.Generic;
using System.Reflection;

namespace BiomeLockAPI
{
    /// <summary>
    /// BiomeLock API for other mods. Reference this DLL and merge it with ILRepack.
    /// Works without BiomeLock installed: queries return neutral values and events never fire.
    /// Add [BepInDependency("radamanto.BiomeLock", BepInDependency.DependencyFlags.SoftDependency)]
    /// to your plugin so BiomeLock loads first. All queries refer to the local player.
    /// </summary>
    public static class API
    {
        private const string ImplAssemblyName = "BiomeLock";
        private const string ImplTypeName = "BiomeLock.BiomeLockAPI";
        private const int ResolveRetryMs = 1000;

        private static Type? _impl;
        private static int _nextResolveTick;

        private static Func<bool>? _isReady;
        private static Func<string, bool>? _hasPrivateKey;
        private static Func<string, bool>? _hasProgressionKey;
        private static Func<IReadOnlyList<string>>? _getPrivateKeys;
        private static Func<Heightmap.Biome, bool>? _isBiomeLocked;
        private static Func<bool>? _isRestricted;

        private static readonly List<(string EventName, Delegate Handler)> Pending = new();

        /// <summary>True if BiomeLock is installed and loaded.</summary>
        public static bool IsLoaded => Resolve();

        /// <summary>True while the local player is in the world and its keys are available.</summary>
        public static bool IsReady => Resolve() && _isReady!();

        /// <summary>Prefix of private keys.</summary>
        public const string PrivatePrefix = "bl_";

        /// <summary>True if the local player has the private key. Accepts "defeated_eikthyr" or "bl_defeated_eikthyr".</summary>
        public static bool HasPrivateKey(string globalOrPrivateKey)
        {
            return globalOrPrivateKey != null && Resolve() && _hasPrivateKey!(globalOrPrivateKey);
        }

        /// <summary>
        /// True if the local player has this progression, following the BiomeLock mode
        /// (private keys when "PrivateKeys" is on, world global keys when it is off).
        /// Without BiomeLock, checks the world global key.
        /// </summary>
        public static bool HasProgressionKey(string globalOrPrivateKey)
        {
            if (globalOrPrivateKey == null) return false;
            if (Resolve()) return _hasProgressionKey!(globalOrPrivateKey);

            string key = ToGlobalKey(globalOrPrivateKey);
            return key.Length > 0 && ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
        }

        /// <summary>Private keys of the local player, sorted. Empty without BiomeLock or outside the world.</summary>
        public static IReadOnlyList<string> GetPrivateKeys()
        {
            return Resolve() ? _getPrivateKeys!() : Array.Empty<string>();
        }

        /// <summary>True if the biome is locked for the local player. Always false without BiomeLock.</summary>
        public static bool IsBiomeLocked(Heightmap.Biome biome)
        {
            return Resolve() && _isBiomeLocked!(biome);
        }

        /// <summary>True if the local player currently has the BiomeLock restriction effect. Always false without BiomeLock.</summary>
        public static bool IsRestricted()
        {
            return Resolve() && _isRestricted!();
        }

        /// <summary>Converts "defeated_eikthyr" to "bl_defeated_eikthyr".</summary>
        public static string ToPrivateKey(string globalOrPrivateKey)
        {
            string key = Normalize(globalOrPrivateKey);
            if (key.Length == 0) return string.Empty;
            return key.StartsWith(PrivatePrefix, StringComparison.Ordinal) ? key : PrivatePrefix + key;
        }

        /// <summary>Converts "bl_defeated_eikthyr" to "defeated_eikthyr".</summary>
        public static string ToGlobalKey(string globalOrPrivateKey)
        {
            string key = Normalize(globalOrPrivateKey);
            return key.StartsWith(PrivatePrefix, StringComparison.Ordinal) ? key.Substring(PrivatePrefix.Length) : key;
        }

        /// <summary>Raised when the local player gains a private key. Argument: the private key ("bl_...").</summary>
        public static event Action<string> OnPrivateKeyAdded
        {
            add => Subscribe(nameof(OnPrivateKeyAdded), value);
            remove => Unsubscribe(nameof(OnPrivateKeyAdded), value);
        }

        /// <summary>Raised when the local player loses a private key. Argument: the private key ("bl_...").</summary>
        public static event Action<string> OnPrivateKeyRemoved
        {
            add => Subscribe(nameof(OnPrivateKeyRemoved), value);
            remove => Unsubscribe(nameof(OnPrivateKeyRemoved), value);
        }

        /// <summary>Raised when all private keys of the local player are removed.</summary>
        public static event Action OnPrivateKeysReset
        {
            add => Subscribe(nameof(OnPrivateKeysReset), value);
            remove => Unsubscribe(nameof(OnPrivateKeysReset), value);
        }

        /// <summary>Raised when the local player's private keys are loaded after entering the world.</summary>
        public static event Action OnPrivateKeysLoaded
        {
            add => Subscribe(nameof(OnPrivateKeysLoaded), value);
            remove => Unsubscribe(nameof(OnPrivateKeysLoaded), value);
        }

        private static string Normalize(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant();

        private static void Subscribe(string eventName, Delegate? handler)
        {
            if (handler == null) return;

            if (Resolve()) _impl!.GetEvent(eventName)?.AddEventHandler(null, handler);
            else Pending.Add((eventName, handler));
        }

        private static void Unsubscribe(string eventName, Delegate? handler)
        {
            if (handler == null) return;

            Pending.RemoveAll(p => p.EventName == eventName && p.Handler == handler);
            if (Resolve()) _impl!.GetEvent(eventName)?.RemoveEventHandler(null, handler);
        }

        private static bool Resolve()
        {
            if (_impl != null) return true;

            int now = Environment.TickCount;
            if (unchecked(now - _nextResolveTick) < 0) return false;
            _nextResolveTick = unchecked(now + ResolveRetryMs);

            Type? impl = FindImplType();
            if (impl == null) return false;

            try
            {
                _isReady = Getter<bool>(impl, "IsReady");
                _hasPrivateKey = Method<Func<string, bool>>(impl, "HasPrivateKey");
                _hasProgressionKey = Method<Func<string, bool>>(impl, "HasProgressionKey");
                _getPrivateKeys = Method<Func<IReadOnlyList<string>>>(impl, "GetPrivateKeys");
                _isBiomeLocked = Method<Func<Heightmap.Biome, bool>>(impl, "IsBiomeLocked");
                _isRestricted = Method<Func<bool>>(impl, "IsRestricted");
            }
            catch
            {
                return false;
            }

            _impl = impl;

            foreach ((string eventName, Delegate handler) in Pending)
                impl.GetEvent(eventName)?.AddEventHandler(null, handler);
            Pending.Clear();

            return true;
        }

        private static Type? FindImplType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != ImplAssemblyName) continue;
                return assembly.GetType(ImplTypeName, false);
            }

            return null;
        }

        private static T Method<T>(Type impl, string name) where T : Delegate
        {
            MethodInfo method = impl.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(ImplTypeName, name);
            return (T)Delegate.CreateDelegate(typeof(T), method);
        }

        private static Func<TResult> Getter<TResult>(Type impl, string name)
        {
            MethodInfo getter = impl.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetGetMethod()
                ?? throw new MissingMemberException(ImplTypeName, name);
            return (Func<TResult>)Delegate.CreateDelegate(typeof(Func<TResult>), getter);
        }
    }
}
