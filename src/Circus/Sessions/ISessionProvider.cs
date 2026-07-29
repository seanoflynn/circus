namespace Circus.Sessions;

public interface ISessionProvider
{
    event EventHandler<SessionStatusChangedArgs> Changed;

    void Update(DateTime current);
}
