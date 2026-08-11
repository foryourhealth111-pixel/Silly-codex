import { sessionKey } from "./session-state.mjs";

const SESSION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/iu;
const CLIENT_PREFIX = "client-new-thread:";
const GLOBAL_STATE_PREFIX = "thread-client-id-v1:";

export const FOCUS_DOM_EXPRESSION = `(() => {
  const row = document.querySelector(
    '[data-app-action-sidebar-thread-selected="true"], ' +
    '[data-app-action-sidebar-thread-active="true"][aria-current="page"]'
  );
  const conversations = Array.from(
    document.querySelectorAll('[data-above-composer-conversation-id]')
  );
  const conversation = conversations.find((item) =>
    Boolean(item.offsetWidth || item.offsetHeight || item.getClientRects().length)
  ) || null;
  return {
    rowId: row ? row.getAttribute('data-app-action-sidebar-thread-id') || '' : '',
    title: row ? row.getAttribute('data-app-action-sidebar-thread-title') || '' : '',
    conversationId: conversation
      ? conversation.getAttribute('data-above-composer-conversation-id') || ''
      : ''
  };
})()`;

function normalizedSessionId(value) {
  const text = typeof value === "string" ? value.trim().toLowerCase() : "";
  return SESSION_ID.test(text) ? text : "";
}

function canonicalForClientAlias(alias, globalState) {
  if (!alias || !globalState || typeof globalState !== "object" || Array.isArray(globalState)) {
    return "";
  }
  const persistedAtoms = globalState["electron-persisted-atom-state"];
  const containers = [globalState];
  if (persistedAtoms && typeof persistedAtoms === "object" && !Array.isArray(persistedAtoms)) {
    containers.push(persistedAtoms);
  }
  for (const container of containers) {
    for (const [key, value] of Object.entries(container)) {
      if (value !== alias || !key.startsWith(GLOBAL_STATE_PREFIX)) continue;
      try {
        const decoded = decodeURIComponent(key.slice(GLOBAL_STATE_PREFIX.length));
        if (!decoded.startsWith("local:")) continue;
        const sessionId = normalizedSessionId(decoded.slice("local:".length));
        if (sessionId) return sessionId;
      } catch {
        // Ignore malformed unrelated global-state entries.
      }
    }
  }
  return "";
}

export function resolveCodexFocus(raw, globalState = null) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
  const rowId = typeof raw.rowId === "string" ? raw.rowId.trim() : "";
  if (!rowId.startsWith("local:")) return null;

  const rawConversationId = typeof raw.conversationId === "string"
    ? raw.conversationId.trim()
    : "";
  const conversationId = normalizedSessionId(rawConversationId);
  if (rawConversationId && !conversationId) return null;

  const localId = rowId.slice("local:".length);
  let sessionId = "";
  let mapping = "";
  if (localId.startsWith(CLIENT_PREFIX)) {
    const persisted = canonicalForClientAlias(localId, globalState);
    if (persisted && conversationId && persisted !== conversationId) return null;
    sessionId = persisted || conversationId;
    mapping = persisted ? "persisted-alias" : "active-dom";
  } else {
    const direct = normalizedSessionId(localId);
    if (!direct || (conversationId && direct !== conversationId)) return null;
    sessionId = direct;
    mapping = "direct";
  }
  if (!sessionId) return null;

  const title = typeof raw.title === "string" ? raw.title.trim().slice(0, 300) : "";
  return {
    version: 1,
    available: true,
    key: sessionKey(sessionId),
    sessionId,
    rowId,
    title,
    mapping,
  };
}
