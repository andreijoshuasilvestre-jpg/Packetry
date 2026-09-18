/// <summary>
/// Central mutual-exclusion gate for the toggle-style interaction modes (cable connect,
/// cable remove, device remove, device drag). Only one may be active at a time; call
/// CancelOthers() before entering a new mode so switching modes always resets whatever
/// was active before it, instead of leaving stale highlights/previews running.
///
/// New interaction modes should register their cancel hook here rather than reaching
/// into every other manager by hand.
/// </summary>
public static class InteractionMode
{
    public static void CancelOthers(object activating)
    {
        if (activating != CableManager.Instance &&
            CableManager.Instance != null && CableManager.Instance.IsConnecting)
            CableManager.Instance.CancelConnecting();

        if (activating != CableRemoverManager.Instance &&
            CableRemoverManager.Instance != null && CableRemoverManager.Instance.IsRemoving)
            CableRemoverManager.Instance.CancelRemoving();

        if (activating != DeviceRemoverManager.Instance &&
            DeviceRemoverManager.Instance != null && DeviceRemoverManager.Instance.IsRemoving)
            DeviceRemoverManager.Instance.CancelRemoving();

        if (activating != TableRemoverManager.Instance &&
            TableRemoverManager.Instance != null && TableRemoverManager.Instance.IsRemoving)
            TableRemoverManager.Instance.CancelRemoving();

        if (activating != TableManager.Instance &&
            TableManager.Instance != null && TableManager.Instance.IsPlacing)
            TableManager.Instance.CancelPlacing();

        if (activating != TableManager.Instance &&
            TableManager.Instance != null && TableManager.Instance.IsDragging)
            TableManager.Instance.CancelDragging();

        if (activating != PingToolManager.Instance &&
            PingToolManager.Instance != null && PingToolManager.Instance.IsPinging)
            PingToolManager.Instance.CancelPinging();

        if (activating != PacketSendManager.Instance &&
            PacketSendManager.Instance != null && PacketSendManager.Instance.IsSending)
            PacketSendManager.Instance.CancelSending();

        if (activating != DragDropManager.Instance && DragDropManager.Instance != null)
        {
            if (DragDropManager.Instance.IsDragging)
                DragDropManager.Instance.CancelDragging();
            DragDropManager.Instance.ClearHighlight();
        }

        if (activating != TableManager.Instance && TableManager.Instance != null)
            TableManager.Instance.ClearHighlight();
    }
}
