document.addEventListener('DOMContentLoaded', () => {
    // --- GLOBAL STATE ---
    let templates = [];
    let actions = [];
    let departments = [];
    let serviceGroups = [];
    let warehouses = [];
    let serviceMappings = {};
    let pharmacyMappings = {};
    let candidates = [];
    let currentEditingTemplate = null;

    // --- DOM ELEMENTS ---
    const themeToggle = document.getElementById('themeToggle');
    const tabBtns = document.querySelectorAll('.tab-btn');
    const tabPanels = document.querySelectorAll('.tab-panel');
    const toast = document.getElementById('toast');

    // Execution Tab Elements
    const examDateInput = document.getElementById('examDate');
    const candNameInput = document.getElementById('candName');
    const candIdInput = document.getElementById('candId');
    const candDeptSelect = document.getElementById('candDept');
    const candTemplateSelect = document.getElementById('candTemplate');
    const scoreConfigContainer = document.getElementById('scoreConfigContainer');
    const scoreFieldsGrid = document.getElementById('scoreFieldsGrid');
    const scoreSummaryBadge = document.getElementById('scoreSummaryBadge');
    const btnAddCandidate = document.getElementById('btnAddCandidate');
    const candidatesList = document.getElementById('candidatesList');
    const selectionSummary = document.getElementById('selectionSummary');
    const btnSelectAllCandidates = document.getElementById('btnSelectAllCandidates');
    const btnClearCandidateSelection = document.getElementById('btnClearCandidateSelection');
    const btnGenerateSelected = document.getElementById('btnGenerateSelected');
    const btnGenerate = document.getElementById('btnGenerate');

    // Templates Tab Elements
    const filterTemplateDept = document.getElementById('filterTemplateDept');
    const btnOpenCreateTemplate = document.getElementById('btnOpenCreateTemplate');
    const templatesGrid = document.getElementById('templatesGrid');
    const templateModal = document.getElementById('templateModal');
    const templateModalTitle = document.getElementById('templateModalTitle');
    const btnCloseTemplateModal = document.getElementById('btnCloseTemplateModal');
    const btnCancelTemplateModal = document.getElementById('btnCancelTemplateModal');
    const btnSaveTemplate = document.getElementById('btnSaveTemplate');
    const tplEditId = document.getElementById('tplEditId');
    const tplName = document.getElementById('tplName');
    const tplDept = document.getElementById('tplDept');
    const tplPosition = document.getElementById('tplPosition');
    const tplScoreTotalBadge = document.getElementById('tplScoreTotalBadge');
    const tplActionsList = document.getElementById('tplActionsList');

    // Clone Modal Elements
    const cloneModal = document.getElementById('cloneModal');
    const cloneSourceId = document.getElementById('cloneSourceId');
    const cloneSourceName = document.getElementById('cloneSourceName');
    const cloneTargetDept = document.getElementById('cloneTargetDept');
    const cloneNewName = document.getElementById('cloneNewName');
    const btnCloseCloneModal = document.getElementById('btnCloseCloneModal');
    const btnCancelCloneModal = document.getElementById('btnCancelCloneModal');
    const btnConfirmClone = document.getElementById('btnConfirmClone');

    // Mappings Tab Elements
    const serviceMatrixHeader = document.getElementById('serviceMatrixHeader');
    const serviceMatrixBody = document.getElementById('serviceMatrixBody');
    const btnSaveServiceMappings = document.getElementById('btnSaveServiceMappings');
    const pharmacyMappingList = document.getElementById('pharmacyMappingList');
    const btnSavePharmacyMappings = document.getElementById('btnSavePharmacyMappings');

    // Catalogs Elements
    const btnReloadCatalogs = document.getElementById('btnReloadCatalogs');
    const scriptModal = document.getElementById('scriptModal');
    const scriptContentArea = document.getElementById('scriptContentArea');
    const btnCloseScriptModal = document.getElementById('btnCloseScriptModal');
    const btnCloseScriptBtn = document.getElementById('btnCloseScriptBtn');
    const btnCopyScript = document.getElementById('btnCopyScript');

    // Inventory Elements
    const invDept = document.getElementById('invDept');
    const invWarehouse = document.getElementById('invWarehouse');
    const invSource = document.getElementById('invSource');
    const invKeyword = document.getElementById('invKeyword');
    const btnSearchInventory = document.getElementById('btnSearchInventory');
    const inventoryResultsBody = document.getElementById('inventoryResultsBody');

    // --- INITIALIZATION ---
    function init() {
        initTheme();
        initTabs();
        setDefaultExamDate();
        loadAllInitialData();
        bindEvents();
    }

    // --- THEME ---
    function initTheme() {
        const savedTheme = localStorage.getItem('theme') || 'light';
        document.documentElement.setAttribute('data-theme', savedTheme);
        themeToggle.addEventListener('click', () => {
            const currentTheme = document.documentElement.getAttribute('data-theme');
            const newTheme = currentTheme === 'light' ? 'dark' : 'light';
            document.documentElement.setAttribute('data-theme', newTheme);
            localStorage.setItem('theme', newTheme);
        });
    }

    // --- TABS ---
    function initTabs() {
        tabBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const targetTab = btn.getAttribute('data-tab');
                tabBtns.forEach(b => b.classList.remove('active'));
                tabPanels.forEach(p => p.classList.remove('active'));
                btn.classList.add('active');
                const panel = document.getElementById(targetTab);
                if (panel) panel.classList.add('active');
                
                // Refresh views on tab change
                if (targetTab === 'templates-tab') renderTemplates();
                if (targetTab === 'mappings-tab') renderMappings();
                if (targetTab === 'settings-tab') loadCatalogsStatus();
            });
        });
    }

    function setDefaultExamDate() {
        const today = new Date().toISOString().split('T')[0];
        examDateInput.value = today;
    }

    // --- TOAST NOTIFICATION ---
    function showToast(message, type = 'info') {
        toast.textContent = message;
        toast.className = `toast toast-${type}`;
        toast.style.display = 'block';
        setTimeout(() => {
            toast.style.display = 'none';
        }, 3500);
    }

    // --- DATA LOADING ---
    async function loadAllInitialData() {
        try {
            await Promise.all([
                loadMetadata(),
                loadActions(),
                loadTemplates(),
                loadMappings(),
                loadCatalogsStatus()
            ]);
            populateDropdowns();
        } catch (e) {
            console.error("Error loading initial data:", e);
            showToast("Lỗi khi tải dữ liệu khởi tạo từ máy chủ.", "error");
        }
    }

    async function loadMetadata() {
        const [deptsRes, svcsRes, whsRes] = await Promise.all([
            fetch('/api/metadata/departments'),
            fetch('/api/metadata/service-groups'),
            fetch('/api/metadata/warehouses')
        ]);
        departments = await deptsRes.json();
        serviceGroups = await svcsRes.json();
        warehouses = await whsRes.json();
    }

    async function loadActions() {
        const res = await fetch('/api/actions');
        actions = await res.json();
    }

    async function loadTemplates() {
        const res = await fetch('/api/templates');
        templates = await res.json();
        renderTemplates();
    }

    async function loadMappings() {
        const [svcRes, pharRes] = await Promise.all([
            fetch('/api/mappings/services'),
            fetch('/api/mappings/pharmacies')
        ]);
        serviceMappings = await svcRes.json();
        pharmacyMappings = await pharRes.json();
    }

    function populateDropdowns() {
        // Candidate Dept dropdown
        candDeptSelect.innerHTML = '<option value="">-- Chọn Khoa / Phòng --</option>';
        filterTemplateDept.innerHTML = '<option value="">-- Tất cả Khoa / Phòng --</option>';
        tplDept.innerHTML = '';
        cloneTargetDept.innerHTML = '';
        invDept.innerHTML = '<option value="">-- Tất cả Khoa --</option>';

        departments.forEach(dept => {
            candDeptSelect.innerHTML += `<option value="${dept}">${dept}</option>`;
            filterTemplateDept.innerHTML += `<option value="${dept}">${dept}</option>`;
            tplDept.innerHTML += `<option value="${dept}">${dept}</option>`;
            cloneTargetDept.innerHTML += `<option value="${dept}">${dept}</option>`;
            invDept.innerHTML += `<option value="${dept}">${dept}</option>`;
        });

        // Inventory Warehouses dropdown
        invWarehouse.innerHTML = '<option value="">-- Tất cả Kho --</option>';
        warehouses.forEach(wh => {
            invWarehouse.innerHTML += `<option value="${wh}">${wh}</option>`;
        });
    }

    // --- TAB 1: EXECUTION & CANDIDATES ---
    candDeptSelect.addEventListener('change', () => {
        const dept = candDeptSelect.value;
        candTemplateSelect.innerHTML = '';
        scoreConfigContainer.style.display = 'none';
        scoreFieldsGrid.innerHTML = '';

        if (!dept) {
            candTemplateSelect.disabled = true;
            candTemplateSelect.innerHTML = '<option value="">-- Chọn Khoa trước --</option>';
            return;
        }

        const deptTemplates = templates.filter(t => t.dept === dept);
        if (deptTemplates.length === 0) {
            candTemplateSelect.disabled = true;
            candTemplateSelect.innerHTML = '<option value="">-- Không có mẫu đề cho khoa này --</option>';
            return;
        }

        candTemplateSelect.disabled = false;
        candTemplateSelect.innerHTML = '<option value="">-- Chọn Mẫu Đề thi --</option>';
        deptTemplates.forEach(t => {
            const hisWarning = (t.uses_his && !t.has_user) ? ' (Thiếu user HIS)' : '';
            candTemplateSelect.innerHTML += `<option value="${t.id}" ${(t.uses_his && !t.has_user) ? 'disabled' : ''}>${t.name}${hisWarning}</option>`;
        });
    });

    candTemplateSelect.addEventListener('change', () => {
        const tplId = candTemplateSelect.value;
        if (!tplId) {
            scoreConfigContainer.style.display = 'none';
            return;
        }

        const tpl = templates.find(t => t.id === tplId);
        if (!tpl || !tpl.actions) {
            scoreConfigContainer.style.display = 'none';
            return;
        }

        renderExecutionScoreFields(tpl);
    });

    function renderExecutionScoreFields(tpl) {
        scoreFieldsGrid.innerHTML = '';
        let total = 0;

        tpl.actions.forEach((act, idx) => {
            const actionDef = actions.find(a => a.code === act.action_code) || {};
            const title = actionDef.name || act.action_code;
            const defaultScore = act.score !== undefined ? act.score : 1.0;
            total += defaultScore;

            const fieldBox = document.createElement('div');
            fieldBox.style.cssText = 'background: rgba(0,0,0,0.15); padding: 0.75rem; border-radius: 8px; border: 1px solid var(--glass-border);';
            fieldBox.innerHTML = `
                <div style="font-size: 0.85rem; font-weight: 600; margin-bottom: 0.35rem; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;" title="${title}">
                    Câu ${idx + 1}: ${title}
                </div>
                <div style="display: flex; gap: 0.5rem; align-items: center;">
                    <input type="number" step="0.5" min="0" max="10" value="${defaultScore}" class="exec-score-input" data-index="${idx}" style="width: 70px; padding: 0.35rem; text-align: center; border-radius: 6px; border: 1px solid var(--glass-border); background: rgba(255,255,255,0.05); color: var(--text-primary);">
                    <label style="font-size: 0.78rem; color: var(--text-secondary); cursor: pointer; display: flex; align-items: center; gap: 3px;">
                        <input type="checkbox" class="exec-skip-check" data-index="${idx}"> Bỏ câu
                    </label>
                </div>
            `;
            scoreFieldsGrid.appendChild(fieldBox);
        });

        updateExecutionScoreSummary();
        scoreConfigContainer.style.display = 'block';

        // Add event listeners to inputs
        scoreFieldsGrid.querySelectorAll('.exec-score-input').forEach(inp => {
            inp.addEventListener('input', updateExecutionScoreSummary);
        });
        scoreFieldsGrid.querySelectorAll('.exec-skip-check').forEach(chk => {
            chk.addEventListener('change', (e) => {
                const idx = e.target.getAttribute('data-index');
                const inp = scoreFieldsGrid.querySelector(`.exec-score-input[data-index="${idx}"]`);
                if (e.target.checked) {
                    inp.dataset.prevScore = inp.value;
                    inp.value = 0;
                    inp.disabled = true;
                } else {
                    inp.value = inp.dataset.prevScore || 1.0;
                    inp.disabled = false;
                }
                updateExecutionScoreSummary();
            });
        });
    }

    function updateExecutionScoreSummary() {
        const inputs = scoreFieldsGrid.querySelectorAll('.exec-score-input');
        let sum = 0;
        inputs.forEach(inp => {
            sum += parseFloat(inp.value) || 0;
        });
        scoreSummaryBadge.textContent = `Tổng: ${sum.toFixed(1)} đ`;
        if (Math.abs(sum - 10.0) < 0.01) {
            scoreSummaryBadge.className = 'badge badge-success';
        } else {
            scoreSummaryBadge.className = 'badge badge-warning';
        }
    }

    btnAddCandidate.addEventListener('click', () => {
        const name = candNameInput.value.trim();
        const id = candIdInput.value.trim();
        const dept = candDeptSelect.value;
        const templateId = candTemplateSelect.value;

        if (!name) {
            showToast("Vui lòng nhập Họ tên thí sinh.", "error");
            return;
        }
        if (!dept) {
            showToast("Vui lòng chọn Khoa / Phòng.", "error");
            return;
        }
        if (!templateId) {
            showToast("Vui lòng chọn Mẫu đề thi.", "error");
            return;
        }

        const template = templates.find(t => t.id === templateId);
        if (!template) {
            showToast("Mẫu đề thi không hợp lệ.", "error");
            return;
        }

        // Collect custom scores
        const customScores = [];
        scoreFieldsGrid.querySelectorAll('.exec-score-input').forEach(inp => {
            customScores.push(parseFloat(inp.value) || 0);
        });

        const newCand = {
            uid: 'c_' + Date.now() + '_' + Math.random().toString(36).substr(2, 5),
            name: name,
            id: id,
            dept: dept,
            template_id: templateId,
            template_name: template.name,
            position: template.position || template.name,
            scores: customScores,
            selected: true
        };

        candidates.push(newCand);
        candNameInput.value = '';
        candIdInput.value = '';
        renderCandidatesList();
        showToast(`Đã thêm thí sinh "${name}" vào danh sách.`, "success");
    });

    function renderCandidatesList() {
        if (candidates.length === 0) {
            candidatesList.innerHTML = '<div class="empty-state">Chưa có thí sinh nào được thêm vào danh sách thi.</div>';
            selectionSummary.textContent = 'Đã chọn 0/0 thí sinh';
            btnGenerate.disabled = true;
            btnGenerateSelected.disabled = true;
            return;
        }

        candidatesList.innerHTML = '';
        const selectedCount = candidates.filter(c => c.selected).length;
        selectionSummary.textContent = `Đã chọn ${selectedCount}/${candidates.length} thí sinh`;
        btnGenerate.disabled = false;
        btnGenerateSelected.disabled = selectedCount === 0;

        candidates.forEach(cand => {
            const row = document.createElement('div');
            row.className = 'candidate-row';
            row.innerHTML = `
                <div style="text-align: center;">
                    <input type="checkbox" class="cand-checkbox" data-uid="${cand.uid}" ${cand.selected ? 'checked' : ''}>
                </div>
                <div>
                    <strong>${cand.name}</strong> ${cand.id ? `<span style="font-size: 0.8rem; color: var(--text-secondary);">(${cand.id})</span>` : ''}
                </div>
                <div>${cand.dept}</div>
                <div style="font-size: 0.85rem;">${cand.template_name}</div>
                <div style="text-align: right;">
                    <button class="btn-icon btn-remove-cand" data-uid="${cand.uid}" title="Xóa thí sinh" style="width: 32px; height: 32px; color: var(--danger-color);">
                        &times;
                    </button>
                </div>
            `;
            candidatesList.appendChild(row);
        });

        candidatesList.querySelectorAll('.cand-checkbox').forEach(chk => {
            chk.addEventListener('change', (e) => {
                const uid = e.target.getAttribute('data-uid');
                const cand = candidates.find(c => c.uid === uid);
                if (cand) cand.selected = e.target.checked;
                renderCandidatesList();
            });
        });

        candidatesList.querySelectorAll('.btn-remove-cand').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const uid = e.currentTarget.getAttribute('data-uid');
                candidates = candidates.filter(c => c.uid !== uid);
                renderCandidatesList();
            });
        });
    }

    btnSelectAllCandidates.addEventListener('click', () => {
        candidates.forEach(c => c.selected = true);
        renderCandidatesList();
    });

    btnClearCandidateSelection.addEventListener('click', () => {
        candidates.forEach(c => c.selected = false);
        renderCandidatesList();
    });

    // --- GENERATION CALLS ---
    btnGenerate.addEventListener('click', () => triggerGeneration(candidates));
    btnGenerateSelected.addEventListener('click', () => {
        const selected = candidates.filter(c => c.selected);
        if (selected.length === 0) {
            showToast("Vui lòng chọn ít nhất 1 thí sinh.", "warning");
            return;
        }
        triggerGeneration(selected);
    });

    async function triggerGeneration(targetCandidates) {
        const examDateVal = examDateInput.value;
        if (!examDateVal) {
            showToast("Vui lòng chọn Ngày thi.", "error");
            return;
        }

        if (!targetCandidates || targetCandidates.length === 0) {
            // Auto add from form if user typed name & selected template
            const name = candNameInput.value.trim();
            const id = candIdInput.value.trim() || `SBD${String(candidates.length + 1).padStart(3, '0')}`;
            const dept = candDeptSelect.value;
            const templateId = candTemplateSelect.value;

            if (name && templateId) {
                const tpl = templates.find(t => t.id === templateId);
                const currentScores = [];
                scoreFieldsGrid.querySelectorAll('.score-input').forEach(inp => {
                    currentScores.push(parseFloat(inp.value) || 0);
                });

                const newCand = {
                    id: id,
                    name: name,
                    dept: dept,
                    template_id: templateId,
                    template_name: tpl ? tpl.name : 'Đề mặc định',
                    position: tpl ? tpl.position : 'Điều dưỡng',
                    scores: currentScores,
                    selected: true
                };
                candidates.push(newCand);
                renderCandidatesList();
                targetCandidates = [newCand];
            } else {
                showToast("Vui lòng thêm ít nhất 1 thí sinh vào danh sách để sinh đề thi.", "warning");
                return;
            }
        }

        const dateParts = examDateVal.split('-');
        const formattedDate = `${dateParts[2]}/${dateParts[1]}/${dateParts[0]}`;

        btnGenerate.disabled = true;
        btnGenerateSelected.disabled = true;
        showToast("Đang sinh đề thi Word và script SQL...", "info");

        try {
            const payload = {
                exam_date: formattedDate,
                candidates: targetCandidates.map(c => ({
                    name: c.name,
                    id: c.id,
                    template_id: c.template_id,
                    position: c.position,
                    scores: c.scores
                }))
            };

            const response = await fetch('/api/generate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                const err = await response.json();
                throw new Error(err.detail || "Lỗi khi sinh đề thi.");
            }

            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `De_Thi_${Date.now()}.zip`;
            document.body.appendChild(a);
            a.click();
            a.remove();
            window.URL.revokeObjectURL(url);

            showToast("Sinh đề thi và tải file ZIP thành công!", "success");
        } catch (err) {
            console.error(err);
            showToast(err.message, "error");
        } finally {
            renderCandidatesList();
        }
    }

    // --- TAB 2: TEMPLATE BUILDER ---
    filterTemplateDept.addEventListener('change', () => {
        renderTemplates();
    });

    function renderTemplates() {
        const filterDept = filterTemplateDept.value;
        const list = filterDept ? templates.filter(t => t.dept === filterDept) : templates;

        templatesGrid.innerHTML = '';
        if (list.length === 0) {
            templatesGrid.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: var(--text-secondary); padding: 3rem;">Chưa có mẫu đề thi nào cho bộ lọc này. Hãy nhấn "Tạo Mẫu đề Mới".</div>';
            return;
        }

        list.forEach(tpl => {
            const card = document.createElement('div');
            card.className = 'template-card';

            let actionsHtml = '';
            let totalScore = 0;
            if (tpl.actions && tpl.actions.length > 0) {
                tpl.actions.forEach((act, idx) => {
                    const actDef = actions.find(a => a.code === act.action_code) || {};
                    const name = actDef.name || act.action_code;
                    const score = act.score !== undefined ? act.score : 1.0;
                    totalScore += score;
                    actionsHtml += `
                        <div class="template-action-item">
                            <span>${idx + 1}. ${name}</span>
                            <strong>${score}đ</strong>
                        </div>
                    `;
                });
            } else {
                actionsHtml = '<div style="font-size: 0.8rem; color: var(--text-secondary); padding: 0.5rem 0;">Mẫu đề cơ bản.</div>';
            }

            const hisBadge = tpl.uses_his
                ? (tpl.has_user ? '<span class="badge badge-info">HIS Sẵn sàng</span>' : '<span class="badge badge-warning">Thiếu User HIS</span>')
                : '<span class="badge" style="background: rgba(139,92,246,0.15); color: #a78bfa;">Văn phòng (Non-HIS)</span>';

            card.innerHTML = `
                <div>
                    <div class="template-card-header">
                        <h3>${tpl.name}</h3>
                        ${hisBadge}
                    </div>
                    <div class="template-card-meta">
                        <div><strong>Khoa:</strong> ${tpl.dept}</div>
                        <div><strong>Vị trí:</strong> ${tpl.position || 'Điều dưỡng'} (${tpl.actions ? tpl.actions.length : 0} câu - Tổng: ${totalScore.toFixed(1)}đ)</div>
                    </div>
                    <div class="template-actions-list">
                        ${actionsHtml}
                    </div>
                </div>
                <div class="template-card-footer">
                    <button class="btn-card btn-clone-tpl" data-id="${tpl.id}" style="padding: 0.35rem 0.65rem; font-size: 0.8rem;">
                        Nhân bản
                    </button>
                    <button class="btn-card btn-edit-tpl" data-id="${tpl.id}" style="padding: 0.35rem 0.65rem; font-size: 0.8rem;">
                        Chỉnh sửa
                    </button>
                    <button class="btn-card btn-delete-tpl" data-id="${tpl.id}" style="padding: 0.35rem 0.65rem; font-size: 0.8rem; color: var(--danger-color);">
                        Xóa
                    </button>
                </div>
            `;
            templatesGrid.appendChild(card);
        });

        // Event listeners for cards
        templatesGrid.querySelectorAll('.btn-edit-tpl').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const id = e.currentTarget.getAttribute('data-id');
                openEditTemplateModal(id);
            });
        });

        templatesGrid.querySelectorAll('.btn-clone-tpl').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const id = e.currentTarget.getAttribute('data-id');
                openCloneModal(id);
            });
        });

        templatesGrid.querySelectorAll('.btn-delete-tpl').forEach(btn => {
            btn.addEventListener('click', async (e) => {
                const id = e.currentTarget.getAttribute('data-id');
                const tpl = templates.find(t => t.id === id);
                if (!tpl) return;
                if (confirm(`Bạn có chắc chắn muốn xóa mẫu đề "${tpl.name}" không?`)) {
                    try {
                        const res = await fetch(`/api/templates/${id}`, { method: 'DELETE' });
                        if (!res.ok) throw new Error("Không thể xóa mẫu đề.");
                        await loadTemplates();
                        showToast("Đã xóa mẫu đề thi.", "success");
                    } catch (err) {
                        showToast(err.message, "error");
                    }
                }
            });
        });
    }

    // --- TEMPLATE CREATE / EDIT MODAL ---
    btnOpenCreateTemplate.addEventListener('click', () => {
        openCreateTemplateModal();
    });

    function openCreateTemplateModal() {
        currentEditingTemplate = null;
        templateModalTitle.textContent = "Tạo Mẫu Đề thi Mới";
        tplEditId.value = "";
        tplName.value = "";
        if (departments && departments.length > 0) {
            tplDept.value = departments[0];
        }
        tplPosition.value = "Điều dưỡng";
        renderActionBuilderRows([]);
        templateModal.style.display = "flex";
    }

    function openEditTemplateModal(id) {
        const tpl = templates.find(t => t.id === id);
        if (!tpl) return;
        currentEditingTemplate = tpl;
        templateModalTitle.textContent = "Chỉnh sửa Mẫu Đề thi";
        tplEditId.value = tpl.id;
        tplName.value = tpl.name;
        tplDept.value = tpl.dept;
        tplPosition.value = tpl.position || "Điều dưỡng";
        renderActionBuilderRows(tpl.actions || []);
        templateModal.style.display = "flex";
    }

    function renderActionBuilderRows(selectedActions) {
        tplActionsList.innerHTML = '';
        const selectedMap = {};
        selectedActions.forEach(a => {
            selectedMap[a.action_code] = a.score !== undefined ? a.score : 1.0;
        });

        actions.forEach(act => {
            const isChecked = selectedMap[act.code] !== undefined;
            const scoreVal = isChecked ? selectedMap[act.code] : (act.default_score || 1.0);

            const row = document.createElement('div');
            row.className = `action-builder-row ${isChecked ? 'active' : ''}`;
            row.dataset.code = act.code;
            row.innerHTML = `
                <div style="display: flex; align-items: center; gap: 0.75rem;">
                    <input type="checkbox" class="act-builder-check" ${isChecked ? 'checked' : ''} style="width: 18px; height: 18px; accent-color: var(--accent-color); cursor: pointer;">
                </div>
                <div class="action-builder-info">
                    <h5>${act.name} <span class="badge" style="font-size: 0.7rem; margin-left: 4px;">${act.category}</span></h5>
                    <p>${act.description}</p>
                </div>
                <div class="action-builder-score">
                    <label>Điểm:</label>
                    <input type="number" step="0.5" min="0.5" max="10" value="${scoreVal}" class="act-builder-score-input" ${isChecked ? '' : 'disabled'}>
                </div>
            `;
            tplActionsList.appendChild(row);
        });

        updateTemplateModalScoreTotal();

        // Event listeners for action builder rows
        tplActionsList.querySelectorAll('.act-builder-check').forEach(chk => {
            chk.addEventListener('change', (e) => {
                const row = e.target.closest('.action-builder-row');
                const scoreInp = row.querySelector('.act-builder-score-input');
                if (e.target.checked) {
                    row.classList.add('active');
                    scoreInp.disabled = false;
                } else {
                    row.classList.remove('active');
                    scoreInp.disabled = true;
                }
                updateTemplateModalScoreTotal();
            });
        });

        tplActionsList.querySelectorAll('.act-builder-score-input').forEach(inp => {
            inp.addEventListener('input', updateTemplateModalScoreTotal);
        });
    }

    function updateTemplateModalScoreTotal() {
        let total = 0;
        tplActionsList.querySelectorAll('.action-builder-row').forEach(row => {
            const chk = row.querySelector('.act-builder-check');
            const scoreInp = row.querySelector('.act-builder-score-input');
            if (chk.checked) {
                total += parseFloat(scoreInp.value) || 0;
            }
        });
        tplScoreTotalBadge.textContent = `Tổng điểm: ${total.toFixed(1)} / 10 đ`;
        if (Math.abs(total - 10.0) < 0.01) {
            tplScoreTotalBadge.className = 'badge badge-success';
        } else {
            tplScoreTotalBadge.className = 'badge badge-warning';
        }
    }

    btnCloseTemplateModal.addEventListener('click', () => templateModal.style.display = 'none');
    btnCancelTemplateModal.addEventListener('click', () => templateModal.style.display = 'none');

    btnSaveTemplate.addEventListener('click', async () => {
        const name = tplName.value.trim();
        const dept = tplDept.value;
        const position = tplPosition.value.trim();
        const editId = tplEditId.value;

        if (!name) {
            showToast("Vui lòng nhập Tên Mẫu Đề thi.", "error");
            return;
        }

        const selectedActions = [];
        tplActionsList.querySelectorAll('.action-builder-row').forEach(row => {
            const chk = row.querySelector('.act-builder-check');
            const scoreInp = row.querySelector('.act-builder-score-input');
            if (chk.checked) {
                selectedActions.push({
                    action_code: row.dataset.code,
                    score: parseFloat(scoreInp.value) || 1.0,
                    params: {}
                });
            }
        });

        if (selectedActions.length === 0) {
            showToast("Vui lòng tích chọn ít nhất 1 nghiệp vụ cho đề thi.", "error");
            return;
        }

        const payload = {
            name: name,
            dept: dept,
            position: position,
            actions: selectedActions
        };

        try {
            let res;
            if (editId) {
                res = await fetch(`/api/templates/${editId}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
            } else {
                res = await fetch('/api/templates', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
            }

            if (!res.ok) throw new Error("Lỗi khi lưu mẫu đề thi.");
            await loadTemplates();
            templateModal.style.display = 'none';
            showToast("Đã lưu Mẫu đề thi thành công!", "success");
        } catch (err) {
            showToast(err.message, "error");
        }
    });

    // --- CLONE MODAL ---
    function openCloneModal(sourceId) {
        const source = templates.find(t => t.id === sourceId);
        if (!source) return;
        cloneSourceId.value = source.id;
        cloneSourceName.value = source.name;
        
        // Pick first available different department
        const targetDept = departments.find(d => d !== source.dept) || departments[0];
        cloneTargetDept.value = targetDept;
        cloneNewName.value = `${source.name} (${targetDept})`;
        cloneModal.style.display = 'flex';
    }

    cloneTargetDept.addEventListener('change', () => {
        const source = templates.find(t => t.id === cloneSourceId.value);
        if (source) {
            cloneNewName.value = `${source.name} (${cloneTargetDept.value})`;
        }
    });

    btnCloseCloneModal.addEventListener('click', () => cloneModal.style.display = 'none');
    btnCancelCloneModal.addEventListener('click', () => cloneModal.style.display = 'none');

    btnConfirmClone.addEventListener('click', async () => {
        const sourceId = cloneSourceId.value;
        const targetDept = cloneTargetDept.value;
        const newName = cloneNewName.value.trim();

        try {
            const res = await fetch(`/api/templates/${sourceId}/clone`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    target_dept: targetDept,
                    new_name: newName
                })
            });
            if (!res.ok) throw new Error("Lỗi khi nhân bản mẫu đề.");
            await loadTemplates();
            cloneModal.style.display = 'none';
            showToast("Nhân bản mẫu đề thi thành công!", "success");
        } catch (err) {
            showToast(err.message, "error");
        }
    });

    // --- TAB 3: MAPPINGS (MASTER-DETAIL SERVICE GROUPS & PHARMACIES) ---
    let selectedMappingDept = "";

    const mappingDeptSearch = document.getElementById('mappingDeptSearch');
    const mappingDeptList = document.getElementById('mappingDeptList');
    const mappingCurrentDeptTitle = document.getElementById('mappingCurrentDeptTitle');
    const mappingCurrentDeptSubtitle = document.getElementById('mappingCurrentDeptSubtitle');
    const btnCopyDeptMapping = document.getElementById('btnCopyDeptMapping');
    const btnSaveCurrentDeptMapping = document.getElementById('btnSaveCurrentDeptMapping');

    const svcGroupSearchInput = document.getElementById('svcGroupSearchInput');
    const svcGroupSuggestionsDropdown = document.getElementById('svcGroupSuggestionsDropdown');
    const selectedServicesTags = document.getElementById('selectedServicesTags');
    const countSelectedServices = document.getElementById('countSelectedServices');
    const btnSelectAllServicesForDept = document.getElementById('btnSelectAllServicesForDept');
    const btnClearServicesForDept = document.getElementById('btnClearServicesForDept');

    const pharmacySearchInput = document.getElementById('pharmacySearchInput');
    const pharmacySuggestionsDropdown = document.getElementById('pharmacySuggestionsDropdown');
    const selectedPharmaciesTags = document.getElementById('selectedPharmaciesTags');
    const countSelectedPharmacies = document.getElementById('countSelectedPharmacies');
    const btnSelectAllPharmaciesForDept = document.getElementById('btnSelectAllPharmaciesForDept');
    const btnClearPharmaciesForDept = document.getElementById('btnClearPharmaciesForDept');

    // Copy Mapping Modal Elements
    const copyMappingModal = document.getElementById('copyMappingModal');
    const copyTargetDeptName = document.getElementById('copyTargetDeptName');
    const copySourceDeptSelect = document.getElementById('copySourceDeptSelect');
    const btnCloseCopyMappingModal = document.getElementById('btnCloseCopyMappingModal');
    const btnCancelCopyMappingModal = document.getElementById('btnCancelCopyMappingModal');
    const btnConfirmCopyMapping = document.getElementById('btnConfirmCopyMapping');

    function renderMappings() {
        if (!selectedMappingDept && departments.length > 0) {
            selectedMappingDept = departments[0];
        }
        renderMappingDeptList();
        renderMappingDetail();
    }

    function renderMappingDeptList() {
        const query = (mappingDeptSearch.value || "").trim().toLowerCase();
        mappingDeptList.innerHTML = '';

        const filteredDepts = departments.filter(d => d.toLowerCase().includes(query));
        if (filteredDepts.length === 0) {
            mappingDeptList.innerHTML = '<div style="font-size: 0.82rem; color: var(--text-secondary); padding: 0.5rem; text-align: center;">Không tìm thấy khoa nào.</div>';
            return;
        }

        filteredDepts.forEach(dept => {
            const numSvcs = (serviceMappings[dept] || []).length;
            const numPhars = (pharmacyMappings[dept] || []).length;
            const isActive = dept === selectedMappingDept;

            const item = document.createElement('div');
            item.className = `mapping-dept-item ${isActive ? 'active' : ''}`;
            item.dataset.dept = dept;
            item.innerHTML = `
                <div class="mapping-dept-item-title">${dept}</div>
                <div class="mapping-dept-item-subtitle">${numSvcs} nhóm dịch vụ • ${numPhars} kho dược</div>
            `;
            item.addEventListener('click', () => {
                selectedMappingDept = dept;
                renderMappingDeptList();
                renderMappingDetail();
            });
            mappingDeptList.appendChild(item);
        });
    }

    mappingDeptSearch.addEventListener('input', () => {
        renderMappingDeptList();
    });

    function renderMappingDetail() {
        if (!selectedMappingDept) return;

        mappingCurrentDeptTitle.textContent = selectedMappingDept;
        const currentSvcs = serviceMappings[selectedMappingDept] || [];
        const currentPhars = pharmacyMappings[selectedMappingDept] || [];
        mappingCurrentDeptSubtitle.textContent = `Đang cấu hình: ${currentSvcs.length} nhóm dịch vụ và ${currentPhars.length} kho dược liên kết.`;

        renderSelectedServicesTags();
        renderSelectedPharmaciesTags();
    }

    // --- SERVICE GROUPS SELECT2 & TAGS ---
    function renderSelectedServicesTags() {
        const currentSvcs = serviceMappings[selectedMappingDept] || [];
        countSelectedServices.textContent = currentSvcs.length;

        if (currentSvcs.length === 0) {
            selectedServicesTags.innerHTML = '<span style="color: var(--text-secondary); font-size: 0.85rem; font-style: italic;">Chưa chọn nhóm dịch vụ nào. Sử dụng thanh tìm kiếm phía trên để thêm.</span>';
            return;
        }

        selectedServicesTags.innerHTML = '';
        currentSvcs.forEach(grp => {
            const chip = document.createElement('span');
            chip.className = 'selected-tag-chip';
            chip.innerHTML = `
                <span>${grp}</span>
                <button class="selected-tag-remove" data-group="${grp}" title="Gỡ nhóm này">&times;</button>
            `;
            chip.querySelector('.selected-tag-remove').addEventListener('click', (e) => {
                e.stopPropagation();
                removeServiceGroupFromDept(grp);
            });
            selectedServicesTags.appendChild(chip);
        });
    }

    function addServiceGroupToDept(grp) {
        if (!serviceMappings[selectedMappingDept]) {
            serviceMappings[selectedMappingDept] = [];
        }
        if (!serviceMappings[selectedMappingDept].includes(grp)) {
            serviceMappings[selectedMappingDept].push(grp);
            renderSelectedServicesTags();
            renderMappingDeptList();
        }
        svcGroupSearchInput.value = '';
        svcGroupSuggestionsDropdown.style.display = 'none';
        svcGroupSearchInput.focus();
    }

    function removeServiceGroupFromDept(grp) {
        if (serviceMappings[selectedMappingDept]) {
            serviceMappings[selectedMappingDept] = serviceMappings[selectedMappingDept].filter(g => g !== grp);
            renderSelectedServicesTags();
            renderMappingDeptList();
        }
    }

    function showServiceGroupSuggestions() {
        const query = svcGroupSearchInput.value.trim().toLowerCase();
        const currentSvcs = serviceMappings[selectedMappingDept] || [];
        const available = serviceGroups.filter(g => g.toLowerCase().includes(query));

        if (available.length === 0) {
            svcGroupSuggestionsDropdown.innerHTML = '<div style="padding: 0.75rem 1rem; font-size: 0.85rem; color: var(--text-secondary);">Không có nhóm dịch vụ phù hợp.</div>';
            svcGroupSuggestionsDropdown.style.display = 'block';
            return;
        }

        svcGroupSuggestionsDropdown.innerHTML = '';
        available.forEach(grp => {
            const isAlreadySelected = currentSvcs.includes(grp);
            const item = document.createElement('div');
            item.className = `select2-suggestion-item ${isAlreadySelected ? 'disabled' : ''}`;
            item.innerHTML = `
                <span>${grp}</span>
                <span style="font-size: 0.75rem; opacity: 0.8;">${isAlreadySelected ? '✓ Đã chọn' : '+ Thêm'}</span>
            `;
            if (!isAlreadySelected) {
                item.addEventListener('click', () => addServiceGroupToDept(grp));
            }
            svcGroupSuggestionsDropdown.appendChild(item);
        });
        svcGroupSuggestionsDropdown.style.display = 'block';
    }

    svcGroupSearchInput.addEventListener('input', showServiceGroupSuggestions);
    svcGroupSearchInput.addEventListener('focus', showServiceGroupSuggestions);

    btnSelectAllServicesForDept.addEventListener('click', () => {
        serviceMappings[selectedMappingDept] = [...serviceGroups];
        renderSelectedServicesTags();
        renderMappingDeptList();
        showToast("Đã chọn tất cả nhóm dịch vụ cho khoa này.", "info");
    });

    btnClearServicesForDept.addEventListener('click', () => {
        serviceMappings[selectedMappingDept] = [];
        renderSelectedServicesTags();
        renderMappingDeptList();
    });

    // --- PHARMACIES SELECT2 & TAGS ---
    function renderSelectedPharmaciesTags() {
        const currentPhars = pharmacyMappings[selectedMappingDept] || [];
        countSelectedPharmacies.textContent = currentPhars.length;

        if (currentPhars.length === 0) {
            selectedPharmaciesTags.innerHTML = '<span style="color: var(--text-secondary); font-size: 0.85rem; font-style: italic;">Chưa liên kết kho dược nào. Sử dụng thanh tìm kiếm phía trên để thêm.</span>';
            return;
        }

        selectedPharmaciesTags.innerHTML = '';
        currentPhars.forEach(wh => {
            const chip = document.createElement('span');
            chip.className = 'selected-tag-chip pharmacy-tag';
            chip.innerHTML = `
                <span>${wh}</span>
                <button class="selected-tag-remove" data-wh="${wh}" title="Gỡ kho này">&times;</button>
            `;
            chip.querySelector('.selected-tag-remove').addEventListener('click', (e) => {
                e.stopPropagation();
                removePharmacyFromDept(wh);
            });
            selectedPharmaciesTags.appendChild(chip);
        });
    }

    function addPharmacyToDept(wh) {
        if (!pharmacyMappings[selectedMappingDept]) {
            pharmacyMappings[selectedMappingDept] = [];
        }
        if (!pharmacyMappings[selectedMappingDept].includes(wh)) {
            pharmacyMappings[selectedMappingDept].push(wh);
            renderSelectedPharmaciesTags();
            renderMappingDeptList();
        }
        pharmacySearchInput.value = '';
        pharmacySuggestionsDropdown.style.display = 'none';
        pharmacySearchInput.focus();
    }

    function removePharmacyFromDept(wh) {
        if (pharmacyMappings[selectedMappingDept]) {
            pharmacyMappings[selectedMappingDept] = pharmacyMappings[selectedMappingDept].filter(w => w !== wh);
            renderSelectedPharmaciesTags();
            renderMappingDeptList();
        }
    }

    function showPharmacySuggestions() {
        const query = pharmacySearchInput.value.trim().toLowerCase();
        const currentPhars = pharmacyMappings[selectedMappingDept] || [];
        const available = warehouses.filter(w => w.toLowerCase().includes(query));

        if (available.length === 0) {
            pharmacySuggestionsDropdown.innerHTML = '<div style="padding: 0.75rem 1rem; font-size: 0.85rem; color: var(--text-secondary);">Không có mã kho phù hợp.</div>';
            pharmacySuggestionsDropdown.style.display = 'block';
            return;
        }

        pharmacySuggestionsDropdown.innerHTML = '';
        available.forEach(wh => {
            const isAlreadySelected = currentPhars.includes(wh);
            const item = document.createElement('div');
            item.className = `select2-suggestion-item ${isAlreadySelected ? 'disabled' : ''}`;
            item.innerHTML = `
                <span>${wh}</span>
                <span style="font-size: 0.75rem; opacity: 0.8;">${isAlreadySelected ? '✓ Đã chọn' : '+ Thêm'}</span>
            `;
            if (!isAlreadySelected) {
                item.addEventListener('click', () => addPharmacyToDept(wh));
            }
            pharmacySuggestionsDropdown.appendChild(item);
        });
        pharmacySuggestionsDropdown.style.display = 'block';
    }

    pharmacySearchInput.addEventListener('input', showPharmacySuggestions);
    pharmacySearchInput.addEventListener('focus', showPharmacySuggestions);

    btnSelectAllPharmaciesForDept.addEventListener('click', () => {
        pharmacyMappings[selectedMappingDept] = [...warehouses];
        renderSelectedPharmaciesTags();
        renderMappingDeptList();
        showToast("Đã chọn tất cả kho dược cho khoa này.", "info");
    });

    btnClearPharmaciesForDept.addEventListener('click', () => {
        pharmacyMappings[selectedMappingDept] = [];
        renderSelectedPharmaciesTags();
        renderMappingDeptList();
    });

    // Close dropdowns on outside click
    document.addEventListener('click', (e) => {
        if (!e.target.closest('.select2-like-container')) {
            svcGroupSuggestionsDropdown.style.display = 'none';
            pharmacySuggestionsDropdown.style.display = 'none';
        }
    });

    // Save All Mappings (for current and all depts)
    btnSaveCurrentDeptMapping.addEventListener('click', async () => {
        try {
            btnSaveCurrentDeptMapping.disabled = true;
            const [resSvc, resPhar] = await Promise.all([
                fetch('/api/mappings/services', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(serviceMappings)
                }),
                fetch('/api/mappings/pharmacies', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(pharmacyMappings)
                })
            ]);

            if (!resSvc.ok || !resPhar.ok) throw new Error("Lỗi khi lưu cấu hình phân quyền.");
            showToast(`Đã lưu cấu hình Dịch vụ & Kho cho "${selectedMappingDept}" thành công!`, "success");
        } catch (err) {
            showToast(err.message, "error");
        } finally {
            btnSaveCurrentDeptMapping.disabled = false;
        }
    });

    // --- COPY MAPPING MODAL ---
    btnCopyDeptMapping.addEventListener('click', () => {
        if (!selectedMappingDept) return;
        copyTargetDeptName.value = selectedMappingDept;
        copySourceDeptSelect.innerHTML = '';
        departments.filter(d => d !== selectedMappingDept).forEach(d => {
            copySourceDeptSelect.innerHTML += `<option value="${d}">${d}</option>`;
        });
        copyMappingModal.style.display = 'flex';
    });

    btnCloseCopyMappingModal.addEventListener('click', () => copyMappingModal.style.display = 'none');
    btnCancelCopyMappingModal.addEventListener('click', () => copyMappingModal.style.display = 'none');

    btnConfirmCopyMapping.addEventListener('click', () => {
        const sourceDept = copySourceDeptSelect.value;
        if (!sourceDept) return;

        serviceMappings[selectedMappingDept] = [...(serviceMappings[sourceDept] || [])];
        pharmacyMappings[selectedMappingDept] = [...(pharmacyMappings[sourceDept] || [])];

        renderMappingDeptList();
        renderMappingDetail();
        copyMappingModal.style.display = 'none';
        showToast(`Đã sao chép phân quyền từ "${sourceDept}" sang "${selectedMappingDept}". Nhấn "Lưu cấu hình" để lưu lại.`, "success");
    });

    // --- TAB 4: SETTINGS & CATALOGS ---
    async function loadCatalogsStatus() {
        try {
            const res = await fetch('/api/catalogs/status');
            const data = await res.json();

            const mapping = {
                'patients.xlsx': { count: 'count-patients', time: 'time-patients', status: 'status-patients' },
                'drugs.xlsx': { count: 'count-drugs', time: 'time-drugs', status: 'status-drugs' },
                'services.xlsx': { count: 'count-services', time: 'time-services', status: 'status-services' },
                'users.xlsx': { count: 'count-users', time: 'time-users', status: 'status-users' },
                'service_mappings.xlsx': { count: 'count-svc-mapping', status: 'status-svc-mapping' },
                'pharmacy_mappings.xlsx': { count: 'count-phar-mapping', status: 'status-phar-mapping' }
            };

            for (const [file, info] of Object.entries(data)) {
                const elMap = mapping[file];
                if (!elMap) continue;

                const countEl = document.getElementById(elMap.count);
                if (countEl) countEl.textContent = info.count ? info.count.toLocaleString() : '0';

                if (elMap.time) {
                    const timeEl = document.getElementById(elMap.time);
                    if (timeEl) timeEl.textContent = info.last_modified || 'Chưa có file';
                }

                const badge = document.getElementById(elMap.status);
                if (badge) {
                    if (info.exists || info.count > 0) {
                        badge.className = 'badge badge-success';
                        badge.textContent = 'Đã nạp';
                    } else {
                        badge.className = 'badge badge-danger';
                        badge.textContent = 'Chưa nạp';
                    }
                }
            }
        } catch (err) {
            console.error("Error loading catalogs status:", err);
        }
    }

    // Helper: Copy to clipboard with robust fallback
    function copyToClipboard(text, successMsg = "Đã sao chép Script vào bộ nhớ tạm!") {
        if (!text) return;
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(() => {
                showToast(successMsg, "success");
            }).catch(() => {
                fallbackCopyText(text, successMsg);
            });
        } else {
            fallbackCopyText(text, successMsg);
        }
    }

    function fallbackCopyText(text, successMsg) {
        try {
            const textArea = document.createElement("textarea");
            textArea.value = text;
            textArea.style.position = "fixed";
            textArea.style.left = "-999999px";
            textArea.style.top = "-999999px";
            textArea.setAttribute("readonly", "");
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();
            const successful = document.execCommand('copy');
            document.body.removeChild(textArea);
            if (successful) {
                showToast(successMsg, "success");
            } else {
                showToast("Đã lấy script. Vui lòng kiểm tra console hoặc thử lại.", "info");
            }
        } catch (err) {
            showToast("Lỗi khi sao chép: " + err, "error");
        }
    }

    btnReloadCatalogs.addEventListener('click', async () => {
        try {
            btnReloadCatalogs.disabled = true;
            const res = await fetch('/api/catalogs/reload', { method: 'POST' });
            if (!res.ok) throw new Error("Không thể làm mới danh mục.");
            await loadAllInitialData();
            showToast("Đã làm mới danh mục dữ liệu thành công!", "success");
        } catch (err) {
            showToast(err.message, "error");
        } finally {
            btnReloadCatalogs.disabled = false;
        }
    });

    // Upload Catalog File
    document.querySelectorAll('.file-upload-input').forEach(input => {
        input.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            const targetFilename = e.target.getAttribute('data-file');
            if (!file) return;

            const formData = new FormData();
            formData.append('file', file);

            try {
                showToast(`Đang tải lên ${targetFilename}...`, "info");
                const res = await fetch(`/api/catalogs/upload/${targetFilename}`, {
                    method: 'POST',
                    body: formData
                });
                if (!res.ok) {
                    const err = await res.json();
                    throw new Error(err.detail || "Lỗi tải file lên.");
                }
                await loadAllInitialData();
                showToast(`Tải lên ${targetFilename} thành công!`, "success");
            } catch (err) {
                showToast(err.message, "error");
            } finally {
                e.target.value = '';
            }
        });
    });

    // Get Script: Auto copy to clipboard directly
    document.querySelectorAll('.btn-get-script').forEach(btn => {
        btn.addEventListener('click', async (e) => {
            const file = e.target.getAttribute('data-file');
            try {
                showToast(`Đang lấy script cho ${file}...`, "info");
                const res = await fetch(`/api/catalogs/script/${file}`);
                if (!res.ok) throw new Error("Không tìm thấy script trích xuất.");
                const sql = await res.text();
                copyToClipboard(sql, `Đã sao chép Script trích xuất "${file}" vào bộ nhớ tạm!`);
            } catch (err) {
                showToast(err.message, "error");
            }
        });
    });

    // Script 07: Services mapping script handlers (Auto copy)
    const handleServiceMappingScript = async () => {
        try {
            showToast("Đang lấy Script 07 (Phân quyền Dịch vụ)...", "info");
            const res = await fetch('/api/mappings/script/services');
            if (!res.ok) throw new Error("Không thể tải Script 07.");
            const sql = await res.text();
            copyToClipboard(sql, "Đã sao chép Script 07 (Phân quyền Dịch vụ) vào bộ nhớ tạm!");
        } catch (err) {
            showToast(err.message, "error");
        }
    };
    const btnGetSvcMappingScript = document.getElementById('btnGetSvcMappingScript');
    if (btnGetSvcMappingScript) btnGetSvcMappingScript.addEventListener('click', handleServiceMappingScript);
    const btnScriptSvcMappingCard = document.getElementById('btnScriptSvcMappingCard');
    if (btnScriptSvcMappingCard) btnScriptSvcMappingCard.addEventListener('click', handleServiceMappingScript);

    // Script 08: Pharmacy mapping script handlers (Auto copy)
    const handlePharmacyMappingScript = async () => {
        try {
            showToast("Đang lấy Script 08 (Ánh xạ Kho Dược)...", "info");
            const res = await fetch('/api/mappings/script/pharmacies');
            if (!res.ok) throw new Error("Không thể tải Script 08.");
            const sql = await res.text();
            copyToClipboard(sql, "Đã sao chép Script 08 (Ánh xạ Kho Dược) vào bộ nhớ tạm!");
        } catch (err) {
            showToast(err.message, "error");
        }
    };
    const btnGetPharMappingScript = document.getElementById('btnGetPharMappingScript');
    if (btnGetPharMappingScript) btnGetPharMappingScript.addEventListener('click', handlePharmacyMappingScript);
    const btnScriptPharMappingCard = document.getElementById('btnScriptPharMappingCard');
    if (btnScriptPharMappingCard) btnScriptPharMappingCard.addEventListener('click', handlePharmacyMappingScript);

    // Upload Service Mapping Excel
    const uploadSvcMappingFile = document.getElementById('uploadSvcMappingFile');
    if (uploadSvcMappingFile) {
        uploadSvcMappingFile.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (!file) return;
            const formData = new FormData();
            formData.append('file', file);
            try {
                showToast("Đang nạp file phân quyền dịch vụ...", "info");
                const res = await fetch('/api/mappings/upload/services', {
                    method: 'POST',
                    body: formData
                });
                const result = await res.json();
                if (!res.ok) throw new Error(result.detail || "Lỗi tải file.");
                await loadMappings();
                renderMappings();
                showToast(result.message, "success");
            } catch (err) {
                showToast(err.message, "error");
            } finally {
                e.target.value = '';
            }
        });
    }

    // Upload Pharmacy Mapping Excel
    const uploadPharMappingFile = document.getElementById('uploadPharMappingFile');
    if (uploadPharMappingFile) {
        uploadPharMappingFile.addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (!file) return;
            const formData = new FormData();
            formData.append('file', file);
            try {
                showToast("Đang nạp file ánh xạ kho dược...", "info");
                const res = await fetch('/api/mappings/upload/pharmacies', {
                    method: 'POST',
                    body: formData
                });
                const result = await res.json();
                if (!res.ok) throw new Error(result.detail || "Lỗi tải file.");
                await loadMappings();
                renderMappings();
                showToast(result.message, "success");
            } catch (err) {
                showToast(err.message, "error");
            } finally {
                e.target.value = '';
            }
        });
    }

    btnCloseScriptModal.addEventListener('click', () => scriptModal.style.display = 'none');
    btnCloseScriptBtn.addEventListener('click', () => scriptModal.style.display = 'none');

    btnCopyScript.addEventListener('click', () => {
        scriptContentArea.select();
        navigator.clipboard.writeText(scriptContentArea.value).then(() => {
            showToast("Đã sao chép script vào bộ nhớ tạm!", "success");
        }).catch(() => {
            showToast("Không thể sao chép script.", "error");
        });
    });

    // --- TAB 5: INVENTORY LOOKUP ---
    btnSearchInventory.addEventListener('click', async () => {
        const dept = invDept.value;
        const wh = invWarehouse.value;
        const src = invSource.value;
        const kw = invKeyword.value.trim();

        inventoryResultsBody.innerHTML = '<tr><td colspan="6" style="text-align: center; padding: 2rem;">Đang tra cứu dữ liệu tồn kho...</td></tr>';

        try {
            const params = new URLSearchParams();
            if (dept) params.append('khoa', dept);
            if (wh) params.append('ma_kho', wh);
            if (src) params.append('nguon', src);
            if (kw) params.append('tu_khoa', kw);

            const res = await fetch(`/api/inventory/search?${params.toString()}`);
            if (!res.ok) throw new Error("Lỗi khi tra cứu tồn kho.");
            const data = await res.json();
            const items = data.items || [];

            if (items.length === 0) {
                inventoryResultsBody.innerHTML = '<tr><td colspan="6" style="text-align: center; color: var(--text-secondary); padding: 2rem;">Không tìm thấy mặt hàng nào phù hợp với bộ lọc.</td></tr>';
                return;
            }

            inventoryResultsBody.innerHTML = '';
            items.slice(0, 100).forEach(item => {
                const tr = document.createElement('tr');
                tr.innerHTML = `
                    <td><code>${item.MaDuoc}</code></td>
                    <td><strong>${item.TenDuoc}</strong></td>
                    <td>${item.DVTTinh}</td>
                    <td>${item.TenKho || item.MaKho}</td>
                    <td><span class="badge ${item.Nguon === 'BH' ? 'badge-success' : 'badge-info'}">${item.Nguon}</span></td>
                    <td class="inventory-stock">${item.SoLuongTon.toLocaleString()}</td>
                `;
                inventoryResultsBody.appendChild(tr);
            });
        } catch (err) {
            inventoryResultsBody.innerHTML = `<tr><td colspan="6" style="text-align: center; color: var(--danger-color); padding: 2rem;">${err.message}</td></tr>`;
        }
    });

    function bindEvents() {
        // Optional global shortcut handlers
    }

    init();
});
