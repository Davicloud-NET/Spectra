using System.IO.Hashing;
internal record FilePayload(long Length, UInt128 Hash)
{
    internal static FilePayload FromFile(string file)
    {
        using var stream = File.OpenRead(file);
        var hash = new XxHash128();
        byte[] buffer = new byte[128 * 1024];
        int count;
        while ((count = stream.Read(buffer)) != 0) hash.Append(buffer.AsSpan(0,count));
        return new(stream.Length, hash.GetCurrentHashAsUInt128());
    }
}
