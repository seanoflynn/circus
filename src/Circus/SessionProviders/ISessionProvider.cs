namespace Circus.SessionProviders;

public interface ISessionProvider
{
    event EventHandler<SessionStatusChangedArgs> Changed;

    void Update(DateTime current);
}
