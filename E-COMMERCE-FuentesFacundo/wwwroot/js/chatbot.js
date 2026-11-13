document.addEventListener('DOMContentLoaded', function () {
    const toggle = document.getElementById('chatbotToggle');
    const panel = document.getElementById('chatbotPanel');
    const closeButton = document.getElementById('chatbotClose');
    const clearButton = document.getElementById('chatbotClear');
    const form = document.getElementById('chatbotForm');
    const input = document.getElementById('chatbotInput');
    const messagesContainer = document.getElementById('chatbotMessages');
    const sendButton = document.getElementById('chatbotSend');
    const storageKey = 'ecommerce-chat-history';

    if (!toggle || !panel || !form || !input || !messagesContainer || !sendButton) {
        return;
    }

    let messages = [];
    try {
        const stored = sessionStorage.getItem(storageKey);
        messages = stored ? JSON.parse(stored) : [];
    } catch (error) {
        messages = [];
    }

    if (!Array.isArray(messages)) {
        messages = [];
    }

    messages = messages
        .filter(function (message) {
            return message &&
                (message.role === 'user' || message.role === 'assistant') &&
                typeof message.content === 'string' &&
                message.content.trim().length > 0;
        })
        .slice(-12);

    if (messages.length === 0) {
        messages.push({
            role: 'assistant',
            content: 'Hola. Soy el asistente de E-COMMERCE. Puedo ayudarte con productos, stock, precios y consultas generales sobre vapeo.'
        });
        saveMessages();
    }

    function saveMessages() {
        try {
            sessionStorage.setItem(storageKey, JSON.stringify(messages));
        } catch (error) {
        }
    }

    function scrollToBottom() {
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }

    function createMessageElement(role, content) {
        const element = document.createElement('div');
        element.className = 'chatbot-message chatbot-message-' + role;
        element.textContent = content;
        return element;
    }

    function renderMessages() {
        messagesContainer.innerHTML = '';
        messages.forEach(function (message) {
            messagesContainer.appendChild(createMessageElement(message.role, message.content));
        });
        scrollToBottom();
    }

    function setBusy(isBusy) {
        sendButton.disabled = isBusy;
        input.disabled = isBusy;
        if (isBusy) {
            const typing = document.createElement('div');
            typing.className = 'chatbot-typing';
            typing.id = 'chatbotTyping';
            typing.textContent = 'Escribiendo...';
            messagesContainer.appendChild(typing);
            scrollToBottom();
        } else {
            const typing = document.getElementById('chatbotTyping');
            if (typing) {
                typing.remove();
            }
        }
    }

    function setOpen(isOpen) {
        panel.hidden = !isOpen;
        toggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        if (isOpen) {
            input.focus();
        }
    }

    async function sendMessage(text) {
        messages.push({ role: 'user', content: text });
        renderMessages();
        saveMessages();
        setBusy(true);

        try {
            const response = await fetch('/api/chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ messages: messages })
            });

            const data = await response.json().catch(function () {
                return {};
            });

            if (!response.ok) {
                throw new Error(data.error || 'No se pudo enviar el mensaje.');
            }

            const reply = typeof data.reply === 'string' && data.reply.trim().length > 0
                ? data.reply
                : 'No recibí una respuesta del asistente.';

            messages.push({ role: 'assistant', content: reply });
            renderMessages();
            saveMessages();
        } catch (error) {
            const message = error instanceof Error ? error.message : 'Error de conexión.';
            messagesContainer.appendChild(createMessageElement('assistant', message));
            scrollToBottom();
        } finally {
            setBusy(false);
            input.focus();
        }
    }

    toggle.addEventListener('click', function () {
        setOpen(panel.hidden);
    });

    if (closeButton) {
        closeButton.addEventListener('click', function () {
            setOpen(false);
        });
    }

    if (clearButton) {
        clearButton.addEventListener('click', function () {
            messages = [];
            try {
                sessionStorage.removeItem(storageKey);
            } catch (error) {
            }
            messages.push({
                role: 'assistant',
                content: 'Historial limpiado. ¿En qué puedo ayudarte?'
            });
            saveMessages();
            renderMessages();
        });
    }

    form.addEventListener('submit', function (event) {
        event.preventDefault();
        if (sendButton.disabled) {
            return;
        }

        const text = input.value.trim();
        if (text.length === 0) {
            return;
        }

        input.value = '';
        sendMessage(text);
    });

    input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            form.requestSubmit();
        }
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape' && !panel.hidden) {
            setOpen(false);
        }
    });

    renderMessages();
});
