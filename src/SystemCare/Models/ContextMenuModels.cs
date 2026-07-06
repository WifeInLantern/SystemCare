namespace SystemCare.Models;

/// <summary>One shell context-menu handler (right-click entry) that can be toggled on/off.</summary>
public record ContextMenuEntry(string KeyPath, string Name, string Location, bool Enabled);
