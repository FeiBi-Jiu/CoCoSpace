using System;
using System.Collections.Generic;
using Game.Proto;
using Google.Protobuf;
using UnityEngine;

namespace Game.Net
{
    public static class MsgDispatcher
    {
        public delegate void Handler<T>(T msg);
        public delegate void RawHandler(ByteString payload);

        static readonly Dictionary<Type, Delegate> handlers = new();
        static readonly Dictionary<MsgId, RawHandler> rawHandlers = new();

        public static void RegisterRaw(MsgId id, RawHandler h)
        {
            if (rawHandlers.TryGetValue(id, out var d))
                rawHandlers[id] = (RawHandler)Delegate.Combine(d, h);
            else
                rawHandlers[id] = h;
        }

        public static void UnregisterRaw(MsgId id) => rawHandlers.Remove(id);

        public static void Register<T>(Handler<T> h) where T : class, IMessage
        {
            if (handlers.TryGetValue(typeof(T), out var d))
                handlers[typeof(T)] = Delegate.Combine(d, h);
            else
                handlers[typeof(T)] = h;
        }

        public static void Unregister<T>() where T : class, IMessage
            => handlers.Remove(typeof(T));

        public static void Unregister<T>(Handler<T> h) where T : class, IMessage
        {
            if (!handlers.TryGetValue(typeof(T), out var d)) return;

            var left = Delegate.Remove(d, h);
            if (left == null) handlers.Remove(typeof(T));
            else handlers[typeof(T)] = left;
        }

        public static void Dispatch(IMessage msg)
        {
            if (msg == null) return;

            var type = msg.GetType();
            if (handlers.TryGetValue(type, out var dlg))
                dlg.DynamicInvoke(msg);
            else
                Debug.LogWarning($"[Msg] no handler for {type.Name}");
        }

        public static void Dispatch(ArraySegment<byte> raw)
        {
            Envelope env;
            try
            {
                env = Envelope.Parser.ParseFrom(raw.Array, raw.Offset, raw.Count);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Msg] bad envelope: {e.Message}");
                return;
            }

            if (!MsgIdMap.IdToType.TryGetValue(env.MsgId, out var type))
            {
                if (rawHandlers.TryGetValue(env.MsgId, out var rawHandler))
                {
                    rawHandler(env.Payload);
                    return;
                }

                Debug.LogWarning($"[Msg] unknown msgId {env.MsgId}");
                return;
            }

            // 用 Parser 反序列化 payload
            var parserProp = type.GetProperty("Parser");
            var parser = (MessageParser)parserProp.GetValue(null);

            IMessage msg;
            try
            {
                msg = parser.ParseFrom(env.Payload);
            }
            catch (Exception e)
            {
                // 冷路径允许下发 JSON 文本（如 MapLayout），protobuf 解析失败时回退到 raw handler
                if (rawHandlers.TryGetValue(env.MsgId, out var fallback))
                {
                    fallback(env.Payload);
                    return;
                }

                Debug.LogError($"[Msg] payload 解析失败 msgId={env.MsgId}: {e.Message}");
                return;
            }

            if (handlers.TryGetValue(type, out var dlg))
                dlg.DynamicInvoke(msg);
            else if (rawHandlers.TryGetValue(env.MsgId, out var rawFallback))
                rawFallback(env.Payload);
            else
                Debug.LogWarning($"[Msg] no handler for {type.Name}");
        }
    }
}