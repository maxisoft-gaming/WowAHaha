namespace WowAHaha;

public interface IRunnableService
{
    Task Run(CancellationToken cancellationToken);

    string Name { get; }
}