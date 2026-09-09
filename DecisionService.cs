using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace LangFixer
{
    /// <summary>
    /// Runs the dictionaries on a worker thread. The low-level keyboard hook is an input-synchronous
    /// callback and Windows refuses outgoing COM calls from it (RPC_E_CANTCALLOUT_ININPUTSYNCCALL),
    /// so the hook must never touch the spell checker directly. It asks this service instead and
    /// waits on a plain kernel event, which is allowed. While a word is still being typed the hook
    /// pre-fetches the verdict, so the wait at the separator is normally a cache hit.
    /// </summary>
    internal sealed class DecisionService : IDisposable
    {
        private sealed class Request
        {
            public Lang TypedIn;
            public bool PrevUnknown;
            public string Typed;
            public string En;
            public string He;
            public Decision Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly Settings _settings;
        private readonly Action<string> _log;
        private readonly BlockingCollection<Request> _queue = new BlockingCollection<Request>();
        private readonly Dictionary<string, Decision> _cache = new Dictionary<string, Decision>();
        private readonly object _cacheLock = new object();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private readonly Thread _thread;
        private Dictionaries _dict;
        private Detector _detector;

        public DecisionService(Settings settings, Action<string> log)
        {
            _settings = settings;
            _log = log ?? delegate { };
            _thread = new Thread(Run) { IsBackground = true, Name = "LangFixer.Dictionaries" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        /// <summary>Blocks until the worker has created the spell checkers.</summary>
        public Dictionaries Dictionaries
        {
            get { _ready.Wait(); return _dict; }
        }

        private void Run()
        {
            _dict = Dictionaries.Create();
            _detector = new Detector(_dict, _settings);
            _ready.Set();
            foreach (var r in _queue.GetConsumingEnumerable())
            {
                try { r.Result = _detector.Decide(r.TypedIn, r.Typed, r.En, r.He, r.PrevUnknown); }
                catch (Exception ex) { r.Result = Decision.Keep("decision error: " + ex.Message); _log("decision error: " + ex); }
                lock (_cacheLock)
                {
                    if (_cache.Count > 2000) _cache.Clear();
                    _cache[Key(r.TypedIn, r.Typed, r.En, r.He, r.PrevUnknown)] = r.Result;
                }
                r.Done.Set();
            }
        }

        private static string Key(Lang typedIn, string typed, string en, string he, bool prevUnknown)
        {
            return (int)typedIn + "|" + (prevUnknown ? "u" : "-") + "|" + typed + "|" + en + "|" + he;
        }

        private Decision Cached(Lang typedIn, string typed, string en, string he, bool prevUnknown)
        {
            lock (_cacheLock)
            {
                Decision d;
                return _cache.TryGetValue(Key(typedIn, typed, en, he, prevUnknown), out d) ? d : null;
            }
        }

        /// <summary>Start computing a verdict for the word as typed so far; never blocks.</summary>
        public void Prefetch(Lang typedIn, string typed, string en, string he, bool prevUnknown)
        {
            if (Cached(typedIn, typed, en, he, prevUnknown) != null) return;
            _queue.Add(new Request { TypedIn = typedIn, Typed = typed, En = en, He = he, PrevUnknown = prevUnknown });
        }

        /// <summary>Verdict for a finished word. Waits at most <paramref name="timeoutMs"/>; on timeout the word is kept.</summary>
        public Decision Decide(Lang typedIn, string typed, string en, string he, bool prevUnknown, int timeoutMs)
        {
            Decision cached = Cached(typedIn, typed, en, he, prevUnknown);
            if (cached != null) return cached;
            var r = new Request { TypedIn = typedIn, Typed = typed, En = en, He = he, PrevUnknown = prevUnknown };
            _queue.Add(r);
            if (r.Done.Wait(timeoutMs)) return r.Result;
            return Decision.Keep("dictionary timeout after " + timeoutMs + "ms");
        }

        /// <summary>Forget cached verdicts (after the ignore list changes).</summary>
        public void ClearCache()
        {
            lock (_cacheLock) _cache.Clear();
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }
}
