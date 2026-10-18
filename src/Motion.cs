using System.Windows;
namespace lumi { internal static class Motion {
// Kept only for old regression callers; no subscriptions, transforms, or animations.
internal static void AutoWire(DependencyObject root) { }
internal static bool IsWired(FrameworkElement element) { return false; }
} }
