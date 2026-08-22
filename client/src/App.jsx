import { useEffect, useRef, useState } from 'react';
import { deleteConversation, fetchTrace, streamChat } from './api.js';

const STORAGE_KEY = 'genai.sessions';

const createSession = () => ({
  key: crypto.randomUUID(),
  conversationId: null, // assigned by the API on the first reply
  title: 'New chat',
  messages: [],
  trace: [],
});

const loadSessions = () => {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
    if (Array.isArray(saved) && saved.length > 0) return saved;
  } catch {
    /* fall through to a fresh session */
  }
  return [createSession()];
};

export default function App() {
  const [sessions, setSessions] = useState(loadSessions);
  const [activeKey, setActiveKey] = useState(() => loadSessions()[0].key);
  const [input, setInput] = useState('');
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState(null);
  const [showTrace, setShowTrace] = useState(true);

  const abortRef = useRef(null);
  const scrollRef = useRef(null);

  const active = sessions.find((s) => s.key === activeKey) ?? sessions[0];

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sessions));
  }, [sessions]);

  // A session opened in another tab (or before a reload) still has its trace on
  // the server, so pull it in when the session is selected.
  useEffect(() => {
    if (!active?.conversationId || active.trace?.length) return;
    let cancelled = false;
    fetchTrace(active.conversationId).then((entries) => {
      if (!cancelled && entries.length) {
        setSessions((prev) =>
          prev.map((s) => (s.key === active.key ? { ...s, trace: entries } : s)),
        );
      }
    });
    return () => {
      cancelled = true;
    };
  }, [active?.key, active?.conversationId]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [active?.messages, isStreaming]);

  /** Applies an update to the active session only. */
  const updateActive = (updater) =>
    setSessions((prev) => prev.map((s) => (s.key === activeKey ? updater(s) : s)));

  const handleNewChat = () => {
    const session = createSession();
    setSessions((prev) => [session, ...prev]);
    setActiveKey(session.key);
    setError(null);
  };

  const handleClearChat = async () => {
    setError(null);
    // Clear server-side history and context too, so the agent truly forgets.
    await deleteConversation(active.conversationId);
    updateActive((s) => ({ ...s, conversationId: null, title: 'New chat', messages: [], trace: [] }));
  };

  const handleStop = () => abortRef.current?.abort();

  const handleSend = async (event) => {
    event.preventDefault();
    const message = input.trim();
    if (!message || isStreaming) return;

    setInput('');
    setError(null);
    setIsStreaming(true);

    // Show the user message plus an empty assistant bubble to stream into.
    updateActive((s) => ({
      ...s,
      title: s.messages.length === 0 ? message.slice(0, 40) : s.title,
      messages: [...s.messages, { role: 'user', text: message }, { role: 'assistant', text: '' }],
    }));

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      const conversationId = await streamChat({
        message,
        conversationId: active.conversationId,
        signal: controller.signal,
        onTrace: (entry) =>
          updateActive((s) =>
            s.trace?.some((e) => e.id === entry.id) ? s : { ...s, trace: [...(s.trace ?? []), entry] },
          ),
        onDelta: (delta) =>
          updateActive((s) => {
            const messages = [...s.messages];
            const last = messages.length - 1;
            messages[last] = { ...messages[last], text: messages[last].text + delta };
            return { ...s, messages };
          }),
      });

      updateActive((s) => ({ ...s, conversationId: conversationId ?? s.conversationId }));
    } catch (err) {
      if (err.name === 'AbortError') {
        updateActive((s) => {
          const messages = [...s.messages];
          const last = messages.length - 1;
          if (!messages[last].text) messages[last] = { ...messages[last], text: '(stopped)' };
          return { ...s, messages };
        });
      } else {
        setError(err.message);
        // Drop the empty assistant bubble so the failure is not mistaken for a reply.
        updateActive((s) => ({ ...s, messages: s.messages.filter((m, i) => !(i === s.messages.length - 1 && m.text === '')) }));
      }
    } finally {
      setIsStreaming(false);
      abortRef.current = null;
    }
  };

  return (
    <div className={`layout ${showTrace ? 'with-trace' : ''}`}>
      <aside className="sidebar">
        <button className="new-chat" onClick={handleNewChat}>+ New chat</button>

        <nav className="session-list">
          {sessions.map((session) => (
            <button
              key={session.key}
              className={`session ${session.key === activeKey ? 'active' : ''}`}
              onClick={() => setActiveKey(session.key)}
            >
              <span className="session-title">{session.title}</span>
              <span className="session-meta">
                {session.conversationId ? `${session.messages.length} msgs` : 'not started'}
              </span>
            </button>
          ))}
        </nav>
      </aside>

      <main className="chat">
        <header className="chat-header">
          <div>
            <h1>GenAI Agent</h1>
            <p className="session-id">
              {active.conversationId ? `session: ${active.conversationId}` : 'session starts on first message'}
            </p>
          </div>
          <div className="header-actions">
            <button
              className={`toggle ${showTrace ? 'on' : ''}`}
              onClick={() => setShowTrace((v) => !v)}
              title="Show what happened behind each reply"
            >
              {showTrace ? 'Hide raw log' : 'Raw log'}
            </button>
            <button className="clear" onClick={handleClearChat} disabled={isStreaming}>
              Clear chat
            </button>
          </div>
        </header>

        <div className="messages" ref={scrollRef}>
          {active.messages.length === 0 && (
            <div className="empty">
              <p>Ask the agent anything. It can check the weather and convert currencies.</p>
              <ul>
                <li>What is the weather in Doha?</li>
                <li>Convert 1000 QAR to INR</li>
              </ul>
            </div>
          )}

          {active.messages.map((message, index) => (
            <div key={index} className={`bubble ${message.role}`}>
              <span className="role">{message.role === 'user' ? 'You' : 'Agent'}</span>
              <div className="text">
                {message.text}
                {isStreaming && index === active.messages.length - 1 && message.role === 'assistant' && (
                  <span className="caret" />
                )}
              </div>
            </div>
          ))}
        </div>

        {error && <div className="error">{error}</div>}

        <form className="composer" onSubmit={handleSend}>
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder="Send a message…"
            disabled={isStreaming}
            autoFocus
          />
          {isStreaming ? (
            <button type="button" className="stop" onClick={handleStop}>Stop</button>
          ) : (
            <button type="submit" disabled={!input.trim()}>Send</button>
          )}
        </form>
      </main>

      {showTrace && <TracePanel entries={active.trace ?? []} isStreaming={isStreaming} />}
    </div>
  );
}

/** Shows the recorded background steps for the active conversation. */
function TracePanel({ entries, isStreaming }) {
  const endRef = useRef(null);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [entries.length]);

  return (
    <aside className="trace">
      <header className="trace-header">
        <h2>Raw log</h2>
        <span className="trace-count">{entries.length} step{entries.length === 1 ? '' : 's'}</span>
      </header>

      <div className="trace-body">
        {entries.length === 0 && (
          <p className="trace-empty">
            Background steps appear here: the request sent to the model, every tool call with its
            arguments and result, and what was written to memory.
          </p>
        )}

        {entries.map((entry) => (
          <div key={entry.id} className={`trace-entry ${entry.category}`}>
            <div className="trace-line">
              <span className={`tag ${entry.category}`}>{entry.category}</span>
              <span className="trace-title">{entry.title}</span>
              {entry.durationMs != null && <span className="trace-ms">{entry.durationMs} ms</span>}
            </div>
            {entry.detail && <pre className="trace-detail">{entry.detail}</pre>}
            <time className="trace-time">{new Date(entry.timestamp).toLocaleTimeString()}</time>
          </div>
        ))}

        {isStreaming && <div className="trace-running">running…</div>}
        <div ref={endRef} />
      </div>
    </aside>
  );
}
