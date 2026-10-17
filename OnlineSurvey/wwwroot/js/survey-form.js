(() => {
    const form = document.querySelector('[data-survey-form]');
    if (!form) return;

    const nameInput = form.querySelector('#RespondentName');
    const emailInput = form.querySelector('#RespondentEmail');
    const phoneInput = form.querySelector('#RespondentPhone');

    const validationAlert = document.getElementById('formValidationAlert');
    const validationMessage = document.getElementById('formValidationMessage');
    const submitBarError = document.getElementById('submitBarError');
    const submitBarErrorText = document.getElementById('submitBarErrorText');
    const toastEl = document.getElementById('surveyToast');
    const toastMessage = document.getElementById('toastMessage');

    let toastInstance = null;
    if (toastEl && window.bootstrap && window.bootstrap.Toast) {
        toastInstance = new window.bootstrap.Toast(toastEl, { delay: 5000 });
    }

    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    const phoneRegex = /^(0|\+84)[35789]\d{8}$/;

    const setFieldError = (input, message) => {
        if (!input) return;
        input.classList.add('is-invalid');
        const container = input.closest('[data-field-group]') || input.parentElement;
        const errorEl = container ? container.querySelector('.field-error') : null;
        if (errorEl) {
            errorEl.textContent = message;
        }
    };

    const clearFieldError = (input) => {
        if (!input) return;
        input.classList.remove('is-invalid');
        const container = input.closest('[data-field-group]') || input.parentElement;
        const errorEl = container ? container.querySelector('.field-error') : null;
        if (errorEl) {
            errorEl.textContent = '';
        }
        checkAndHideAlertsIfClean();
    };

    if (nameInput) {
        nameInput.addEventListener('input', () => clearFieldError(nameInput));
    }
    if (emailInput) {
        emailInput.addEventListener('input', () => clearFieldError(emailInput));
    }
    if (phoneInput) {
        phoneInput.addEventListener('input', () => clearFieldError(phoneInput));
    }

    const questionSections = [...form.querySelectorAll('[data-required="True"], [data-required="true"]')];
    const hasValue = section => [...section.querySelectorAll('input, textarea')]
        .some(input => (input.type === 'checkbox' || input.type === 'radio') ? input.checked : input.value.trim() !== '');

    const clearQuestionError = section => {
        section.classList.remove('border-danger');
        section.classList.remove('shadow-sm');
        const error = section.querySelector('.question-error');
        if (error) error.textContent = '';
        checkAndHideAlertsIfClean();
    };

    form.addEventListener('input', event => {
        const section = event.target.closest('[data-required]');
        if (section) clearQuestionError(section);
    });

    form.addEventListener('change', event => {
        const section = event.target.closest('[data-required]');
        if (section) clearQuestionError(section);
    });

    const checkAndHideAlertsIfClean = () => {
        const hasInputError = form.querySelectorAll('.is-invalid').length > 0;
        const hasQuestionError = form.querySelectorAll('.question-error:not(:empty)').length > 0;
        if (!hasInputError && !hasQuestionError) {
            if (validationAlert) validationAlert.classList.add('d-none');
            if (submitBarError) {
                submitBarError.classList.add('d-none');
                submitBarError.classList.remove('d-flex');
            }
        }
    };

    form.addEventListener('submit', event => {
        let firstInvalid = null;
        const missingItems = [];

        // 1. Validate Họ và tên
        if (nameInput) {
            clearFieldError(nameInput);
            const val = nameInput.value.trim();
            if (!val) {
                setFieldError(nameInput, 'Vui lòng nhập họ và tên của bạn.');
                missingItems.push('Họ và tên');
                firstInvalid ||= nameInput;
            } else if (val.length > 120) {
                setFieldError(nameInput, 'Họ và tên không được vượt quá 120 ký tự.');
                missingItems.push('Họ và tên quá dài');
                firstInvalid ||= nameInput;
            }
        }

        // 2. Validate Email
        if (emailInput) {
            clearFieldError(emailInput);
            const val = emailInput.value.trim();
            if (!val) {
                setFieldError(emailInput, 'Vui lòng nhập địa chỉ email.');
                missingItems.push('Email');
                firstInvalid ||= emailInput;
            } else if (!emailRegex.test(val)) {
                setFieldError(emailInput, 'Địa chỉ email không đúng định dạng (ví dụ: example@gmail.com).');
                missingItems.push('Email chưa đúng định dạng');
                firstInvalid ||= emailInput;
            } else if (val.length > 150) {
                setFieldError(emailInput, 'Email không được vượt quá 150 ký tự.');
                firstInvalid ||= emailInput;
            }
        }

        // 3. Validate Số điện thoại
        if (phoneInput) {
            clearFieldError(phoneInput);
            const raw = phoneInput.value.trim();
            const clean = raw.replace(/[\s.-]/g, '');
            if (!raw) {
                setFieldError(phoneInput, 'Vui lòng nhập số điện thoại.');
                missingItems.push('Số điện thoại');
                firstInvalid ||= phoneInput;
            } else if (!phoneRegex.test(clean)) {
                setFieldError(phoneInput, 'Số điện thoại không hợp lệ. Vui lòng nhập số điện thoại gồm 10 chữ số (ví dụ: 0912345678).');
                missingItems.push('Số điện thoại chưa đúng định dạng');
                firstInvalid ||= phoneInput;
            }
        }

        // 4. Validate Các câu hỏi bắt buộc
        let unansweredQuestionsCount = 0;
        questionSections.forEach(section => {
            clearQuestionError(section);
            if (!hasValue(section)) {
                section.classList.add('border-danger');
                section.classList.add('shadow-sm');
                const error = section.querySelector('.question-error');
                if (error) error.textContent = 'Vui lòng trả lời câu hỏi này.';
                unansweredQuestionsCount++;
                firstInvalid ||= section;
            }
        });

        if (unansweredQuestionsCount > 0) {
            missingItems.push(`${unansweredQuestionsCount} câu hỏi bắt buộc`);
        }

        if (firstInvalid) {
            event.preventDefault();

            // Hiển thị thông báo chi tiết
            const summaryText = 'Bạn chưa hoàn thành: ' + missingItems.join(', ') + '. Vui lòng kiểm tra lại các mục màu đỏ!';

            // 1. Alert Banner đầu form
            if (validationAlert && validationMessage) {
                validationMessage.textContent = summaryText;
                validationAlert.classList.remove('d-none');
            }

            // 2. Thông báo ngay tại thanh Submit bar
            if (submitBarError && submitBarErrorText) {
                submitBarErrorText.textContent = summaryText;
                submitBarError.classList.remove('d-none');
                submitBarError.classList.add('d-flex');
            }

            // 3. Floating Toast nổi bật góc trên màn hình
            if (toastMessage) {
                toastMessage.textContent = summaryText;
            }
            if (toastInstance) {
                toastInstance.show();
            }

            // Cuộn mượt đến mục lỗi đầu tiên và focus
            firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
            if (typeof firstInvalid.focus === 'function') {
                setTimeout(() => firstInvalid.focus(), 300);
            }
        } else {
            // Đóng các thông báo nếu hợp lệ
            if (validationAlert) validationAlert.classList.add('d-none');
            if (submitBarError) {
                submitBarError.classList.add('d-none');
                submitBarError.classList.remove('d-flex');
            }

            const submitBtn = form.querySelector('button[type="submit"]');
            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.innerHTML = 'Đang gửi câu trả lời...';
            }
        }
    });
})();
