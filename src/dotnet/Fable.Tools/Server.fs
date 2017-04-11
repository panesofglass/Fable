module Fable.Tools.Server

open System
open System.IO
open System.Text
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks

let [<Literal>] SIGTERM = "[SIGTERM]"

// From http://www.fssnip.net/hx/title/AsyncAwaitTask-with-timeouts
type Microsoft.FSharp.Control.Async with
    static member AwaitTask (t : Task<'T>, timeout : int) =
        async {
            if timeout >= 0
            then
                use cts = new CancellationTokenSource()
                let timer = Task.Delay (timeout, cts.Token)
                let! completed = Async.AwaitTask <| Task.WhenAny(t, timer)
                if completed = (t :> Task) then
                    cts.Cancel ()
                    let! result = Async.AwaitTask t
                    return Some result
                else return None
            else
                let! result = Async.AwaitTask t
                return Some result
        }

let rec private loop timeout (server: TcpListener) (buffer: byte[]) (onMessage: (string*(string->unit)->unit)) = async {
    try
        // printfn "Waiting for connection..."
        let! client = Async.AwaitTask(server.AcceptTcpClientAsync(), timeout)
        match client with
        | Some client ->
            let stream = client.GetStream()
            let i = stream.Read(buffer, 0, buffer.Length)
            let data = Encoding.UTF8.GetString(buffer, 0, i)
            if data = SIGTERM
            then
                printfn "Closing server..."
                return ()
            else
                onMessage(data, fun (reply: string) ->
                    let msg = Encoding.UTF8.GetBytes(reply)
                    stream.Write(msg, 0, msg.Length)
                    #if NETFX
                    client.Close()
                    #else
                    client.Dispose()
                    #endif
                )
                return! loop timeout server buffer onMessage
        | None ->
            printfn "Timeout (%ims) reached. Closing server..." timeout
            return ()
    with ex ->
        printfn "TCP ERROR: %s" ex.Message
        return ()
}

let start port timeout onMessage =
    let cts = new CancellationTokenSource()
    let buffer = Array.zeroCreate<byte> 8192
    let server = new TcpListener(IPAddress.Parse("127.0.0.1"), port)
    server.Start()
    printfn "Fable server started on port %i%s" port
        (if timeout >= 0 then sprintf " (timeout %ims)" timeout else "")
    loop timeout server buffer onMessage

let stop port = async {
    use client = new TcpClient()
    do! client.ConnectAsync(IPAddress.Parse("127.0.0.1"), port) |> Async.AwaitTask
    let data = Encoding.UTF8.GetBytes(SIGTERM)
    use stream = client.GetStream()
    stream.Write(data, 0, data.Length)
    while client.Connected do
        do! Async.Sleep 200
}
