namespace WowAHaha;

public interface IRunnableService
{
    Task Run(CancellationToken cancellationToken);

    string Name { get; }

    int Priority => 0;
}