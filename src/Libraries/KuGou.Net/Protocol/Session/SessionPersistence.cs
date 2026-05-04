namespace KuGou.Net.Protocol.Session;

public interface ISessionPersistence
{
    KgSession? Load();
    void Save(KgSession session);
    void Clear();
}

public sealed class InMemorySessionPersistence : ISessionPersistence
{
    public KgSession? Load()
    {
        return null;
    }

    public void Save(KgSession session)
    {
    }

    public void Clear()
    {
    }
}
