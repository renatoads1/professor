(() => {
    const formatPhone = (value) => {
        const digits = value.replace(/\D/g, "").slice(0, 11);
        if (digits.length === 0) return "";
        if (digits.length <= 2) return `(${digits}`;

        const areaCode = digits.slice(0, 2);
        const subscriber = digits.slice(2);
        const subscriberGroupSize = subscriber.startsWith("9") ? 5 : 4;
        const prefix = subscriber.slice(0, subscriberGroupSize);
        const suffix = subscriber.slice(subscriberGroupSize);

        return `(${areaCode}) ${prefix}${suffix ? `-${suffix}` : ""}`;
    };

    const caretPositionAfterDigits = (formatted, digitCount) => {
        if (digitCount === 0) return 0;

        let seenDigits = 0;
        for (let index = 0; index < formatted.length; index++) {
            if (/\d/.test(formatted[index])) seenDigits++;
            if (seenDigits === digitCount) return index + 1;
        }

        return formatted.length;
    };

    document.addEventListener("input", (event) => {
        const input = event.target;
        if (!(input instanceof HTMLInputElement) || input.dataset.phoneMask !== "br") return;

        const selectionStart = input.selectionStart ?? input.value.length;
        const digitsBeforeCaret = input.value.slice(0, selectionStart).replace(/\D/g, "").length;
        const formatted = formatPhone(input.value);

        if (input.value !== formatted) {
            input.value = formatted;
            const caretPosition = caretPositionAfterDigits(formatted, digitsBeforeCaret);
            input.setSelectionRange(caretPosition, caretPosition);
            input.dispatchEvent(new Event("input", { bubbles: true }));
        }
    });
})();
