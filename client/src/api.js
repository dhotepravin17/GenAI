const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://localhost:5217';

/**
 * Sends a message and streams the reply.
 * The API returns server-sent events, each a JSON AgentStreamChunk.
 */
export async function streamChat({ message, conversationId, signal, onDelta, onTrace }) {
  const response = await fetch(`${API_BASE}/api/agent/chat/stream`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message, conversationId: conversationId ?? null }),
    signal,
  });

  if (!response.ok) {
    // Validation errors and other failures come back as JSON problem details.
    let detail = `Request failed with status ${response.status}.`;
    try {
      const problem = await response.json();
      detail = problem.detail ?? problem.title ?? detail;
    } catch {
      /* keep the default message */
    }
    throw new Error(detail);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let resolvedId = conversationId ?? null;

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // SSE frames are separated by a blank line; keep any partial frame buffered.
    const frames = buffer.split('\n\n');
    buffer = frames.pop() ?? '';

    for (const frame of frames) {
      const dataLine = frame.split('\n').find((line) => line.startsWith('data:'));
      if (!dataLine) continue;

      const chunk = JSON.parse(dataLine.slice(5).trim());

      if (chunk.conversationId) resolvedId = chunk.conversationId;
      if (chunk.error) throw new Error(chunk.error);
      if (chunk.trace) onTrace?.(chunk.trace);
      if (chunk.delta) onDelta(chunk.delta);
    }
  }

  return resolvedId;
}

/** Deletes a conversation's history and context on the server. */
export async function deleteConversation(conversationId) {
  if (!conversationId) return;
  await fetch(`${API_BASE}/api/agent/conversations/${encodeURIComponent(conversationId)}`, {
    method: 'DELETE',
  });
}

/** Loads the recorded background steps for a conversation. */
export async function fetchTrace(conversationId) {
  if (!conversationId) return [];
  const response = await fetch(
    `${API_BASE}/api/agent/conversations/${encodeURIComponent(conversationId)}/trace`,
  );
  return response.ok ? response.json() : [];
}
