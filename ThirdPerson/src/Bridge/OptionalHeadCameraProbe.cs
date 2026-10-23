#nullable enable
using System;
using System.Reflection;
using PreLuna.ThirdPerson.Contracts;

namespace PreLuna.ThirdPerson.Bridge
{
    public readonly record struct HeadCameraObservation(bool Available, bool PreviewVisible,
        bool ControlsBody, bool ReplacesMainView, PlayerIdentity Subject);
    public sealed class OptionalHeadCameraProbe : IDisposable
    {
        delegate bool ReadIdentity(out bool preview, out bool control, out bool replaces,
            out long agent, out int instance, out long owner, out int slot);
        readonly Func<Type?> resolve;
        readonly Action changed;
        ReadIdentity? read;
        Func<long, int, bool>? ownsCamera;
        EventInfo? changedEvent;
        bool invalid, disposed;
        volatile bool dirty = true;
        public string Status { get; private set; } = "unresolved";
        public OptionalHeadCameraProbe(Func<Type?> resolve, Action changed) { this.resolve = resolve; this.changed = changed; }
        public void Invalidate() => dirty = true;
        void Resolve()
        {
            if (!dirty || disposed) return;
            dirty = false; Unsubscribe(); read = null; ownsCamera = null; invalid = false;
            var type = resolve();
            if (type == null) { Status = "absent"; return; }
            var version = type.GetField("HudIdentityVersion", BindingFlags.Public | BindingFlags.Static);
            if (version?.GetRawConstantValue() is not int v || v != 1) { invalid = true; Status = "unsupported-api"; return; }
            read = type.GetMethod("TryGetHudViewIdentity", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<ReadIdentity>();
            ownsCamera = type.GetMethod("IsHudCaptureCamera", BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<Func<long, int, bool>>();
            changedEvent = type.GetEvent("HudViewIdentityChanged", BindingFlags.Public | BindingFlags.Static);
            invalid = read == null || ownsCamera == null || changedEvent?.EventHandlerType != typeof(Action);
            if (invalid) { Status = "unsupported-api"; changedEvent = null; return; }
            changedEvent!.AddEventHandler(null, changed); Status = "ready";
        }
        public HeadCameraObservation Read()
        {
            if (disposed) return default;
            try
            {
                Resolve();
                if (invalid) return default;
                if (read == null) return new(true, false, false, false, default);
                if (!read(out bool preview, out bool control, out bool replaces, out long agent, out int instance, out long owner, out int slot))
                { Status = "not-ready"; return default; }
                Status = "ready"; return new(true, preview, control, replaces, new(new(agent, instance), owner, slot));
            }
            catch { Status = "probe-error"; dirty = true; return default; }
        }
        public bool IsCaptureCamera(ObjectIdentity camera)
        { try { Resolve(); return ownsCamera?.Invoke(camera.NativeHandle, camera.InstanceId) == true; } catch { Status = "probe-error"; dirty = true; return false; } }
        void Unsubscribe() { try { changedEvent?.RemoveEventHandler(null, changed); } catch { } changedEvent = null; }
        public void Dispose() { if (disposed) return; disposed = true; Unsubscribe(); read = null; ownsCamera = null; }
    }
}
