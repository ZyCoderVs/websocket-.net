using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.WebSockets;
using WebChat.Models;

namespace WebChat
{
    /// <summary>
    /// Chat 的摘要说明
    /// </summary>
    public class Chat : IHttpHandler
    {
        private const int buffSize = 4096;
        private static List<WebSocket> _sockets = new List<WebSocket>();
        private static Object objLock = new object();
        private static Object msgLock = new object();
        private static List<ChatData> _hismsg = new List<ChatData>();
        public void ProcessRequest(HttpContext context)
        {
            if (context.Request.Params["get"] == "get")
            {
                context.Response.Write(JsonConvert.SerializeObject(_hismsg));
            }
            if (context.IsWebSocketRequest)
            {
                context.AcceptWebSocketRequest(Acceptor);
            }
        }
        private async Task Acceptor(AspNetWebSocketContext context)
        {
            var socket = context.WebSocket;
            if (_sockets.Count > 100)
            {
                await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "本房间人满，请稍后进入...........", CancellationToken.None);
                return;
            }
            lock (objLock)
            {
                _sockets.Add(socket);
            }
            byte[] data = new byte[buffSize];
            string userName = context.QueryString["userName"];
            ChatData chatData = new ChatData() { info = userName + "进入房间，当前在线人数为" + _sockets.Count.ToString(), date = DateTime.Now.ToString(), name = "【***系统***】" };
            await SendToAllUsers(chatData);
            //保持连接
            while (true)
            {
                try
                {
                    var coming = await socket.ReceiveAsync(new ArraySegment<byte>(data), CancellationToken.None);
                    if (coming.MessageType == WebSocketMessageType.Close)
                    {
                        lock (objLock)
                        {
                            _sockets.Remove(socket);
                        }
                        chatData = new ChatData() { info = userName + "离开房间，还有" + _sockets.Count.ToString() + "人", date = DateTime.Now.ToString(), name = "【***系统***】" };
                        await SendToAllUsers(chatData);
                        break;
                    }
                    var chatMsg = await ArrayToString(new ArraySegment<byte>(data, 0, coming.Count));
                    if (chatMsg == "heartbeat")
                    {
                        continue;
                    }
                    chatData = JsonConvert.DeserializeObject<ChatData>(chatMsg);
                    chatData.date = DateTime.Now.ToString();
                    await SendToAllUsers(chatData);
                }
                catch (Exception ex)
                {
                    _sockets.Remove(socket);
                    chatData = new ChatData() { info = userName + " 离开房间。还剩" + _sockets.Count.ToString() + "人~~" };
                    await SendToAllUsers(chatData);
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "未知异常 ...", CancellationToken.None);
                }
            }
        }

        private async Task<string> ArrayToString(ArraySegment<byte> arraySegment)
        {
            //return await Task.Run(() =>
            //{
            //    return Encoding.UTF8.GetString(arraySegment.Array.ToArray());
            //});
            using (var ms = new MemoryStream())
            {
                ms.Write(arraySegment.Array, arraySegment.Offset, arraySegment.Count);
                ms.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(ms, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }

        private async Task SendToAllUsers(ChatData chatData)
        {
            SaveHisMsg(chatData);
            var chat = JsonConvert.SerializeObject(chatData);
            var buffer = Encoding.UTF8.GetBytes(chat);
            ArraySegment<byte> array = new ArraySegment<byte>(buffer);
            for (int i = 0; i < _sockets.Count; i++)
            {
                if (_sockets[i].State == WebSocketState.Open)
                {
                    await _sockets[i].SendAsync(array, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }

        private void SaveHisMsg(ChatData chatData)
        {
            var size = 40;
            if (chatData != null)
            {
                lock (msgLock)
                {
                    _hismsg.Add(chatData);
                }
            }
            if (_hismsg.Count >= size)
            {
                lock (msgLock)
                {
                    _hismsg.RemoveRange(0, 30);
                }
            }

        }

        public bool IsReusable
        {
            get
            {
                return false;
            }
        }
    }
}