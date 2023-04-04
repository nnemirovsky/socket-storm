namespace SocketStorm;

internal static class TaskExtensions
{
    public static Task WhenAll(this IEnumerable<Task> tasks) => Task.WhenAll(tasks);

    public static Task WhenAny(this IEnumerable<Task> tasks) => Task.WhenAny(tasks);

    public static void WaitAll(this IEnumerable<Task> tasks) => Task.WaitAll(tasks.ToArray());

    public static void WaitAny(this IEnumerable<Task> tasks) => Task.WaitAny(tasks.ToArray());
}
