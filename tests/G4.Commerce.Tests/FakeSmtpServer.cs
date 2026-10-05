using System.Net;
using System.Net.Sockets;
using System.Text;

public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource stop=new();
    private readonly Task server;
    private readonly string behavior;
    private readonly TaskCompletionSource<string> message=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public Task<string> Received=>message.Task;
    public FakeSmtpServer(string behavior="accept") { this.behavior=behavior;listener.Start(); server=ServeAsync(); }
    private async Task ServeAsync()
    {
        try
        {
            using var client=await listener.AcceptTcpClientAsync(stop.Token);
            await using var stream=client.GetStream();
            using var reader=new StreamReader(stream,Encoding.UTF8);
            await using var writer=new StreamWriter(stream,Encoding.ASCII) { AutoFlush=true,NewLine="\r\n" };
            await writer.WriteLineAsync("220 localhost test SMTP");
            while(!stop.IsCancellationRequested)
            {
                var line=await reader.ReadLineAsync(stop.Token); if(line==null)break;
                if(line.StartsWith("EHLO")||line.StartsWith("HELO"))await writer.WriteLineAsync("250 localhost");
                else if(line.StartsWith("MAIL")||line.StartsWith("RCPT")||line.StartsWith("RSET"))await writer.WriteLineAsync("250 OK");
                else if(line=="DATA")
                {
                    if(behavior=="reject") { await writer.WriteLineAsync("451 Temporary rejection");continue; }
                    await writer.WriteLineAsync("354 End with dot");
                    var text=new StringBuilder();
                    while((line=await reader.ReadLineAsync(stop.Token))!=null && line!=".")text.AppendLine(line);
                    message.TrySetResult(text.ToString());
                    if(behavior=="timeout")await Task.Delay(Timeout.Infinite,stop.Token);
                    await writer.WriteLineAsync("250 Accepted");
                }
                else if(line=="QUIT") { await writer.WriteLineAsync("221 Bye"); break; }
                else await writer.WriteLineAsync("250 OK");
            }
        }
        catch(Exception ex) when(stop.IsCancellationRequested) { _=ex; }
        catch(Exception ex) { message.TrySetException(ex); }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await server; stop.Dispose(); }
}
