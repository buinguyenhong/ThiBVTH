using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed class WordExamWriter
{
    private const string FontFamily = "Times New Roman";

    public void Create(string filePath, ExamScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        using var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        AppendScenarioToBody(body, scenario);

        body.Append(CreateSectionProperties());
        mainPart.Document.Save();
    }

    public void CreateMerged(string filePath, IReadOnlyList<ExamScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        if (scenarios.Count == 0) return;

        using var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        for (var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            AppendScenarioToBody(body, scenario);

            if (i < scenarios.Count - 1)
            {
                // Page break between candidates to ensure clean 2-page print per candidate
                var breakP = new Paragraph(
                    new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                    new Run(new Break { Type = BreakValues.Page })
                );
                body.Append(breakP);
            }
        }

        body.Append(CreateSectionProperties());
        mainPart.Document.Save();
    }

    public void CreateAnswerKeyDocument(string filePath, IReadOnlyList<ExamScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        if (scenarios.Count == 0) return;

        using var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        // Header Table
        var headerTable = CreateAnswerKeyHeaderTable(scenarios[0]);
        body.Append(headerTable);
        AddEmptyParagraph(body, 60);

        // Title
        AddCenteredTitle(body, "BẢNG ĐÁP ÁN & DỮ LIỆU ĐỐI SOÁT CHẤM THI", 14);
        var subText = $"Đợt thi: {scenarios[0].BatchName}   |   Ngày thi: {scenarios[0].ExamDate:dd/MM/yyyy}   |   Tổng số thí sinh: {scenarios.Count}";
        var pSub = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "100", Line = "260", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Center }
        ));
        pSub.Append(new Run(CreateRunProps(20, isItalic: true), new Text(subText) { Space = SpaceProcessingModeValues.Preserve }));
        body.Append(pSub);

        // Table of Answer Keys
        var table = CreateAnswerKeyTable(scenarios);
        body.Append(table);

        body.Append(CreateLandscapeSectionProperties());
        mainPart.Document.Save();
    }

    private static SectionProperties CreateSectionProperties()
    {
        var sectionProps = new SectionProperties();
        var pageSize = new PageSize { Width = 11906, Height = 16838 }; // A4 in dxa
        var pageMargin = new PageMargin { Top = 567, Right = 567, Bottom = 567, Left = 1134 }; // 10mm top/bottom/right, 20mm left
        sectionProps.Append(pageSize, pageMargin);
        return sectionProps;
    }

    private static SectionProperties CreateLandscapeSectionProperties()
    {
        var sectionProps = new SectionProperties();
        var pageSize = new PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape }; // A4 Landscape
        var pageMargin = new PageMargin { Top = 567, Right = 567, Bottom = 567, Left = 567 }; // 10mm margins
        sectionProps.Append(pageSize, pageMargin);
        return sectionProps;
    }

    private static Table CreateAnswerKeyHeaderTable(ExamScenario sample)
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Dxa, Width = "15704" },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None }
            )
        );
        table.AppendChild(tableProps);

        var row = new TableRow();
        var leftCell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "8500" }));
        AddCellLine(leftCell, "BỆNH VIỆN ĐA KHOA THIỆN HẠNH", 11, bold: true, align: JustificationValues.Left);
        AddCellLine(leftCell, "HỘI ĐỒNG THI TUYỂN DỤNG & ĐÁNH GIÁ NĂNG LỰC", 10, bold: false, align: JustificationValues.Left);

        var rightCell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "7204" }));
        AddCellLine(rightCell, "TÀI LIỆU DÀNH CHO CÁN BỘ COI THI & CHẤM THI", 11, bold: true, align: JustificationValues.Right);
        AddCellLine(rightCell, $"Xuất dữ liệu: {DateTime.Now:dd/MM/yyyy HH:mm}", 10, bold: false, isItalic: true, align: JustificationValues.Right);

        row.Append(leftCell, rightCell);
        table.Append(row);
        return table;
    }

    private static Table CreateAnswerKeyTable(IReadOnlyList<ExamScenario> scenarios)
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Dxa, Width = "15704" },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" }
            )
        );
        table.AppendChild(tableProps);

        // Header Row: STT (600) | SBD (900) | Thí sinh & Khoa (2400) | User HIS (1300) | Chi tiết Đáp án / Dữ liệu chấm thi (9304) | Điểm (1200) = 15704
        var headerRow = new TableRow();
        headerRow.Append(
            CreateDrugTableCell("STT", 600, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("SBD", 900, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("Thí sinh & Khoa", 2400, true, JustificationValues.Left, isHeader: true),
            CreateDrugTableCell("User HIS", 1300, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("Dữ liệu câu hỏi & Đáp án đối soát trên HIS", 9304, true, JustificationValues.Left, isHeader: true),
            CreateDrugTableCell("Điểm", 1200, true, JustificationValues.Center, isHeader: true)
        );
        table.Append(headerRow);

        for (var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            var row = new TableRow();

            // STT
            row.Append(CreateDrugTableCell((i + 1).ToString(), 600, false, JustificationValues.Center));

            // SBD
            row.Append(CreateDrugTableCell(string.IsNullOrWhiteSpace(s.CandidateId) ? "-" : s.CandidateId, 900, true, JustificationValues.Center));

            // Candidate & Dept
            var candCell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "2400" }));
            AddCellLine(candCell, s.CandidateName, 10, bold: true);
            AddCellLine(candCell, $"Khoa: {s.DepartmentName}", 9);
            AddCellLine(candCell, $"Mẫu: {s.TemplateName}", 9, isItalic: true);
            row.Append(candCell);

            // User HIS
            var userCell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "1300" }));
            AddCellLine(userCell, string.IsNullOrWhiteSpace(s.HisUserCode) ? "USER_THI" : s.HisUserCode, 10, bold: true, align: JustificationValues.Center);
            AddCellLine(userCell, "PW: 123", 9, align: JustificationValues.Center);
            row.Append(userCell);

            // Answers / Check data
            var detailCell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "9304" }));
            foreach (var q in s.Questions)
            {
                var qTitle = $"Câu {q.OrderIndex} ({q.Score:0.#}đ): {q.Title.Trim()}";
                var pTitle = new Paragraph(new ParagraphProperties(
                    new SpacingBetweenLines { Before = "30", After = "10", Line = "220", LineRule = LineSpacingRuleValues.Auto }
                ));
                pTitle.Append(new Run(CreateRunProps(18, isBold: true), new Text(qTitle) { Space = SpaceProcessingModeValues.Preserve }));
                detailCell.Append(pTitle);

                var pDetail = new Paragraph(new ParagraphProperties(
                    new Indentation { Left = "140" },
                    new SpacingBetweenLines { Before = "0", After = "30", Line = "220", LineRule = LineSpacingRuleValues.Auto }
                ));

                var detailText = BuildQuestionAnswerSummary(s, q);
                pDetail.Append(new Run(CreateRunProps(17), new Text(detailText) { Space = SpaceProcessingModeValues.Preserve }));
                detailCell.Append(pDetail);
            }
            row.Append(detailCell);

            // Total Score
            row.Append(CreateDrugTableCell($"{s.TotalScore:0.#}đ", 1200, true, JustificationValues.Center));

            table.Append(row);
        }

        return table;
    }

    private static string BuildQuestionAnswerSummary(ExamScenario s, ScenarioQuestion q)
    {
        return q.ActionCode switch
        {
            "LT_BAN_PHIEU_KHAM" =>
                $"• Tiếp nhận 1 (VP): {(s.SecondPatient ?? s.Patient).FullName} (Khám nội VP) | Tiếp nhận 2 (BHYT): {s.Patient.FullName} (Khám ngoại tổng hợp BHYT)",

            "LT_THU_PHI_VA_CLS" =>
                $"• Thu phí BN: {(s.SecondPatient ?? s.Patient).FullName} | Mã: {(s.SecondPatient ?? s.Patient).MedicalCode} | Dịch vụ CLS: theo chỉ định trên HIS",

            "LT_THU_TAM_UNG_NOI_TRU" =>
                (s.AdvancePaymentCase1 is not null
                    ? $"• BN nội trú: {s.AdvancePaymentCase1.FullName} (BA: {s.AdvancePaymentCase1.SoBenhAn}, Khoa: {s.AdvancePaymentCase1.DepartmentName}, Giường: {s.AdvancePaymentCase1.BedCode}, Tiền: 1.500.000đ)"
                    : "• BN nội trú (1.500.000đ)") +
                (s.AdvancePaymentOutpatientCase is not null
                    ? $" | BN ngoại trú: {s.AdvancePaymentOutpatientCase.FullName} (BA: {s.AdvancePaymentOutpatientCase.SoBenhAn}, Khoa: {s.AdvancePaymentOutpatientCase.DepartmentName}, Tiền: 500.000đ)"
                    : (s.AdvancePaymentCase2 is not null ? $" | BN ngoại trú: {s.AdvancePaymentCase2.FullName} (BA: {s.AdvancePaymentCase2.SoBenhAn}, Tiền: 500.000đ)" : "")),

            "LT_DOI_DICH_VU_CLS" =>
                $"• Đổi dịch vụ CLS BN: {(s.SecondPatient ?? s.Patient).FullName} (Mã: {(s.SecondPatient ?? s.Patient).MedicalCode}) | Thao tác đổi chỉ định kỹ thuật",

            "LT_TRA_CUU_THONG_TIN" =>
                (s.InpatientLookupCase is not null
                    ? $"• Tra cứu BN nội trú: {s.InpatientLookupCase.FullName} (BA: {s.InpatientLookupCase.SoBenhAn}, Khoa: {s.InpatientLookupCase.DepartmentName}, Giường: {s.InpatientLookupCase.BedCode}, Ngày vào: {s.InpatientLookupCase.AdmissionDate})"
                    : "") +
                (s.OutpatientLookupCase is not null
                    ? $" | HOẶC Tra cứu BN ngoại trú: {s.OutpatientLookupCase.FullName} (Mã: {s.OutpatientLookupCase.MedicalCode}, Số BA: {s.OutpatientLookupCase.SoBenhAn}, Khoa: {s.OutpatientLookupCase.DepartmentName})"
                    : ""),

            "LT_THANH_TOAN_RA_VIEN" =>
                s.DischargeCase is not null
                    ? $"• Thanh toán viện phí ra viện BN: {s.DischargeCase.FullName} (BA: {s.DischargeCase.SoBenhAn}, Khoa: {s.DischargeCase.DepartmentName}, Vào viện: {s.DischargeCase.AdmissionDate})"
                    : "• Thanh toán ra viện bệnh nhân nội trú theo đề bài",

            "LT_TAO_THE_TAM_BHYT" =>
                $"• Cấp thẻ tạm BHYT: Bé gái (Sinh {s.ExamDate.AddMonths(-1):dd/MM/yyyy}, trước ngày thi 1 tháng) | Mã BHYT tạm: TE1... | Hạn thẻ: 30 ngày",

            "YL_CHI_DINH_THUOC_VTYT" =>
                $"• BN: {s.Patient.FullName} (Mã: {s.Patient.MedicalCode}) | Thuốc: " +
                (s.OrderedDrugs.Count > 0 ? string.Join("; ", s.OrderedDrugs.Select(d => $"{d.Name} ({d.Code}, SL:{d.Quantity})")) : "Theo chỉ định"),

            _ =>
                $"• BN: {s.Patient.FullName} (Mã: {s.Patient.MedicalCode})"
        };
    }

    private static void AppendScenarioToBody(Body body, ExamScenario scenario)
    {
        // 1. Header: Left (Hospital + Candidate info) & Right (Examination Council)
        var headerTable = CreateBorderlessHeaderTable(scenario);
        body.Append(headerTable);
        AddEmptyParagraph(body, 60);

        // 2. Table 1: Score & Proctor/Marker Signatures
        var scoreSignTable = CreateScoreSignTable();
        body.Append(scoreSignTable);
        AddEmptyParagraph(body, 80);

        // 3. Title: BÀI THI VI TÍNH
        AddCenteredTitle(body, "BÀI THI VI TÍNH", 15);

        // 4. Questions
        foreach (var q in scenario.Questions)
        {
            var titleText = q.Title.Trim();
            if (titleText.EndsWith(".")) titleText = titleText.Substring(0, titleText.Length - 1).Trim();
            var qHeader = $"câu {q.OrderIndex}) {titleText} ({q.Score:0.#}đ):";
            AddQuestionHeader(body, qHeader);

            var lines = (q.Instruction ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Số điểm đạt được:", StringComparison.OrdinalIgnoreCase))
                {
                    AddScoreParagraph(body, trimmed);
                }
                else if (trimmed.StartsWith("+") || trimmed.StartsWith("-") || trimmed.StartsWith("*") || trimmed.StartsWith("©"))
                {
                    AddBulletParagraph(body, trimmed);
                }
                else
                {
                    AddBodyParagraph(body, line);
                }
            }
            AddEmptyParagraph(body, 30);
        }

        // 5. Table of Drugs & Medical Supplies (only if exam has drug ordering question and ordered drugs exist)
        var hasDrugOrder = scenario.Questions.Any(q => q.ActionCode.Equals("YL_CHI_DINH_THUOC_VTYT", StringComparison.OrdinalIgnoreCase));
        if (hasDrugOrder && scenario.OrderedDrugs.Count > 0)
        {
            AddSectionTitle(body, "Danh sách thuốc, VTYT cần lên y lệnh cho bệnh nhân:");
            var drugTable = CreateDrugsTable(scenario.OrderedDrugs);
            body.Append(drugTable);
            AddEmptyParagraph(body, 60);
        }

        // 6. Footer Note: User & Password
        var userCode = string.IsNullOrWhiteSpace(scenario.HisUserCode) ? "USER_THI" : scenario.HisUserCode;
        AddFooterNote(body, $"Lưu ý : User đăng nhập chương trình: {userCode}; PassWord : 123");

        var isFrontdesk = scenario.Questions.Any(q => q.ActionCode.StartsWith("LT_", StringComparison.OrdinalIgnoreCase)) ||
                          scenario.TemplateId.Equals("tpl-lt-thuphi", StringComparison.OrdinalIgnoreCase);
        if (isFrontdesk)
        {
            AddFooterNote(body, "Ghi chú: Thí sinh kiểm tra kỹ Quyển hoá đơn / Ký hiệu hoá đơn khi in (sử dụng quyển THI hoặc VAT_THI theo quy định). Thí sinh không thao tác trên dữ liệu bệnh nhân ngoài danh sách đề bài giao.");
        }
    }

    private static Table CreateBorderlessHeaderTable(ExamScenario scenario)
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Dxa, Width = "10205" },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None }
            )
        );
        table.AppendChild(tableProps);

        var row = new TableRow();

        // Left column: Hospital and candidate info (6000 dxa)
        var leftCell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "6000" }));
        AddCellLine(leftCell, "BỆNH VIỆN ĐA KHOA THIỆN HẠNH", 12, bold: true, align: JustificationValues.Left);
        AddCellLine(leftCell, $"Họ và tên: {scenario.CandidateName}", 11, bold: false, align: JustificationValues.Left);
        AddCellLine(leftCell, $"Khoa :  {scenario.DepartmentName}", 11, bold: false, align: JustificationValues.Left);
        AddCellLine(leftCell, $"Ngày thi :  {scenario.ExamDate:dd/MM/yyyy}", 11, bold: false, align: JustificationValues.Left);
        AddCellLine(leftCell, $"Thời gian làm bài  :  {scenario.ExamDurationMinutes} phút", 11, bold: false, align: JustificationValues.Left);

        // Right column: Examination Council (4205 dxa)
        var rightCell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "4205" }));
        AddCellLine(rightCell, "HỘI ĐỒNG THI TUYỂN DỤNG", 12, bold: true, align: JustificationValues.Center);

        row.Append(leftCell, rightCell);
        table.Append(row);
        return table;
    }

    private static Table CreateScoreSignTable()
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Dxa, Width = "10205" },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" }
            )
        );
        table.AppendChild(tableProps);

        var row = new TableRow();

        // Col 1: Điểm (2500 dxa)
        var cell1 = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "2500" }));
        AddCellLine(cell1, "Điểm:", 12, bold: true, underline: true, align: JustificationValues.Center);
        for (var i = 0; i < 4; i++) AddCellEmptyLine(cell1, 12);

        // Col 2: Chữ ký của cán bộ chấm thi (4300 dxa)
        var cell2 = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "4300" }));
        AddCellLine(cell2, "Chữ ký của cán bộ chấm thi:", 12, bold: true, align: JustificationValues.Center);
        for (var i = 0; i < 4; i++) AddCellEmptyLine(cell2, 12);

        // Col 3: Chữ ký của cán bộ coi thi (3405 dxa)
        var cell3 = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "3405" }));
        AddCellLine(cell3, "Chữ ký của cán bộ coi thi:", 12, bold: true, align: JustificationValues.Center);
        for (var i = 0; i < 4; i++) AddCellEmptyLine(cell3, 12);

        row.Append(cell1, cell2, cell3);
        table.Append(row);
        return table;
    }

    private static Table CreateDrugsTable(IReadOnlyList<ScenarioDrug> drugs)
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Dxa, Width = "10205" },
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" }
            )
        );
        table.AppendChild(tableProps);

        // Header Row: STT (700) | MaDuoc (1500) | Tên (4805) | ĐVT (1000) | SL (800) | Nguồn (1400) = 10205
        var headerRow = new TableRow();
        headerRow.Append(
            CreateDrugTableCell("STT", 700, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("MaDuoc", 1500, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("Tên", 4805, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("ĐVT", 1000, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("SL", 800, true, JustificationValues.Center, isHeader: true),
            CreateDrugTableCell("Nguồn", 1400, true, JustificationValues.Center, isHeader: true)
        );
        table.Append(headerRow);

        for (var i = 0; i < drugs.Count; i++)
        {
            var d = drugs[i];
            var nguon = d.FundingSource.Contains("BHYT", StringComparison.OrdinalIgnoreCase) ? "BH" : "VP";
            var dataRow = new TableRow();
            dataRow.Append(
                CreateDrugTableCell((i + 1).ToString(), 700, false, JustificationValues.Center),
                CreateDrugTableCell(d.Code, 1500, false, JustificationValues.Center),
                CreateDrugTableCell(d.Name, 4805, false, JustificationValues.Left),
                CreateDrugTableCell(d.Unit, 1000, false, JustificationValues.Center),
                CreateDrugTableCell(d.Quantity.ToString(), 800, false, JustificationValues.Center),
                CreateDrugTableCell(nguon, 1400, false, JustificationValues.Center)
            );
            table.Append(dataRow);
        }

        return table;
    }

    private static TableCell CreateDrugTableCell(string text, int widthDxa, bool isBold, JustificationValues align, bool isHeader = false)
    {
        var cellProps = new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = widthDxa.ToString() });
        if (isHeader)
        {
            cellProps.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "F1F5F9" });
        }

        var cell = new TableCell(cellProps);
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "30", After = "30", Line = "240", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = align }
        ));

        var run = new Run(CreateRunProps(20, isBold: isBold), new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);
        cell.Append(p);
        return cell;
    }

    private static void AddCellLine(TableCell cell, string text, int sizePt, bool bold = false, bool isItalic = false, bool underline = false, JustificationValues? align = null)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "20", After = "20", Line = "250", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = align ?? JustificationValues.Left }
        ));
        var run = new Run(CreateRunProps(sizePt * 2, isBold: bold, isItalic: isItalic, isUnderline: underline), new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);
        cell.Append(p);
    }

    private static void AddCellEmptyLine(TableCell cell, int sizePt)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "20", After = "20", Line = "250", LineRule = LineSpacingRuleValues.Auto }
        ));
        var run = new Run(CreateRunProps(sizePt * 2), new Text("") { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);
        cell.Append(p);
    }

    private static void AddCenteredTitle(Body body, string text, int sizePt)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "100", After = "140", Line = "280", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Center }
        ));
        var run = new Run(CreateRunProps(sizePt * 2, isBold: true), new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);
        body.Append(p);
    }

    private static void AddQuestionHeader(Body body, string headerText)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "80", After = "30", Line = "260", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Left }
        ));
        var run = new Run(CreateRunProps(23, isBold: true), new Text(headerText) { Space = SpaceProcessingModeValues.Preserve }); // 11.5pt
        p.Append(run);
        body.Append(p);
    }

    private static void AddBodyParagraph(Body body, string text)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "20", After = "25", Line = "260", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Left }
        ));
        var run = new Run(CreateRunProps(22), new Text(text) { Space = SpaceProcessingModeValues.Preserve }); // 11pt
        p.Append(run);
        body.Append(p);
    }

    private static void AddBulletParagraph(Body body, string text)
    {
        var p = new Paragraph(new ParagraphProperties(
            new Indentation { Left = "280" },
            new SpacingBetweenLines { Before = "20", After = "30", Line = "260", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Left }
        ));

        // Highlight label prefix before ':' with bold font if present
        var colonIdx = text.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 45)
        {
            var prefix = text.Substring(0, colonIdx + 1);
            var suffix = text.Substring(colonIdx + 1);

            var runPrefix = new Run(CreateRunProps(22, isBold: true), new Text(prefix) { Space = SpaceProcessingModeValues.Preserve });
            var runSuffix = new Run(CreateRunProps(22, isBold: false), new Text(suffix) { Space = SpaceProcessingModeValues.Preserve });
            p.Append(runPrefix, runSuffix);
        }
        else
        {
            var run = new Run(CreateRunProps(22), new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            p.Append(run);
        }

        body.Append(p);
    }

    private static void AddScoreParagraph(Body body, string scoreText)
    {
        var p = new Paragraph(new ParagraphProperties(
            new Indentation { Left = "140" },
            new SpacingBetweenLines { Before = "25", After = "60", Line = "250", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Left }
        ));
        var run = new Run(CreateRunProps(21, isItalic: true), new Text(scoreText) { Space = SpaceProcessingModeValues.Preserve }); // 10.5pt
        p.Append(run);
        body.Append(p);
    }

    private static void AddSectionTitle(Body body, string title)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "80", After = "40", Line = "260", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Left }
        ));
        var run = new Run(CreateRunProps(22, isBold: true), new Text(title) { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);
        body.Append(p);
    }

    private static void AddFooterNote(Body body, string noteText)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "80", After = "100", Line = "260", LineRule = LineSpacingRuleValues.Auto },
            new Justification { Val = JustificationValues.Left }
        ));
        var run = new Run(CreateRunProps(22, isBold: true, isItalic: true), new Text(noteText) { Space = SpaceProcessingModeValues.Preserve });
        p.Append(run);
        body.Append(p);
    }

    private static void AddEmptyParagraph(Body body, int sizeInDxa)
    {
        var p = new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = sizeInDxa.ToString() }
        ));
        body.Append(p);
    }

    private static RunProperties CreateRunProps(int sizeHalfPt, bool isBold = false, bool isItalic = false, bool isUnderline = false)
    {
        var rPr = new RunProperties(
            new RunFonts { Ascii = FontFamily, HighAnsi = FontFamily, ComplexScript = FontFamily },
            new FontSize { Val = sizeHalfPt.ToString() },
            new FontSizeComplexScript { Val = sizeHalfPt.ToString() }
        );
        if (isBold) rPr.Append(new Bold());
        if (isItalic) rPr.Append(new Italic());
        if (isUnderline) rPr.Append(new Underline { Val = UnderlineValues.Single });
        return rPr;
    }
}
