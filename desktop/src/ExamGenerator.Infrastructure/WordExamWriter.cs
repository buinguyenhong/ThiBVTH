using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ExamGenerator.Domain;

namespace ExamGenerator.Infrastructure;

public sealed class WordExamWriter
{
    public void Create(string filePath, ExamScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        using var document = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        // Page setup: Standard A4, 2cm margins
        var sectionProps = new SectionProperties();
        var pageSize = new PageSize { Width = 11906, Height = 16838 }; // A4 in dxa
        var pageMargin = new PageMargin { Top = 1134, Right = 1134, Bottom = 1134, Left = 1134 }; // 20mm
        sectionProps.Append(pageSize, pageMargin);

        // Header: Hospital & Examination Council
        AddParagraph(body, "BỆNH VIỆN ĐA KHOA THIỆN HẠNH", true, JustificationValues.Center, 13);
        AddParagraph(body, "HỘI ĐỒNG THI TUYỂN DỤNG", true, JustificationValues.Center, 12);
        AddParagraph(body, "BÀI THI THỰC HÀNH TRÊN PHẦN MỀM HIS", true, JustificationValues.Center, 16);
        AddParagraph(body, "", false, size: 6);

        // Candidate Info Table
        var candidateTable = CreateStyledTable();
        AddTableRow(candidateTable, "Đợt thi:", scenario.BatchName, "Ngày thi:", $"{scenario.ExamDate:dd/MM/yyyy}");
        AddTableRow(candidateTable, "Họ tên thí sinh:", scenario.CandidateName, "Số báo danh (SBD):", string.IsNullOrWhiteSpace(scenario.CandidateId) ? "(Tự do)" : scenario.CandidateId);
        AddTableRow(candidateTable, "Khoa / Phòng:", scenario.DepartmentName, "Mẫu đề thi:", scenario.TemplateName);
        AddTableRow(candidateTable, "Tài khoản HIS:", $"{scenario.HisUserCode}  (Mật khẩu máy chủ: 123)", "Thang điểm:", $"{scenario.TotalScore:0.0} điểm");
        body.Append(candidateTable);
        AddParagraph(body, "", false, size: 6);

        // Patient Admin Box
        AddParagraph(body, "I. THÔNG TIN NGƯỜI BỆNH KHẢO THÍ", true, JustificationValues.Left, 12);
        var p = scenario.Patient;
        var patTable = CreateStyledTable();
        var birthStr = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToString("dd/MM/yyyy") : (p.Age > 0 ? $"{p.Age} tuổi" : "1990");
        var bhytStr = !string.IsNullOrWhiteSpace(p.InsuranceNumber) 
            ? $"{p.InsuranceNumber} (Hạn: {p.InsurancePeriod?.From:dd/MM/yyyy} - {p.InsurancePeriod?.To:dd/MM/yyyy})" 
            : "Viện phí (Không BHYT)";

        AddTableRow(patTable, "Mã Y tế (PID):", p.MedicalCode, "Họ và tên:", p.FullName.ToUpperInvariant());
        AddTableRow(patTable, "Ngày sinh / Tuổi:", $"{birthStr} ({p.Gender})", "Đối tượng KCB:", bhytStr);
        AddTableRow(patTable, "Nơi ĐKKCB ban đầu:", $"{p.InitialRegistrationCode} - BVĐK Thiện Hạnh", "Địa chỉ liên hệ:", p.Address);
        AddTableRow(patTable, "Chẩn đoán ban đầu:", p.Diagnosis, "Hình thức tiếp nhận:", scenario.RequiresDirectReception ? "Thí sinh tự tiếp nhận" : "Hàng chờ nhận vào khoa");
        body.Append(patTable);
        AddParagraph(body, "", false, size: 8);

        // Exam Questions
        AddParagraph(body, "II. NỘI DUNG YÊU CẦU BÀI THI", true, JustificationValues.Left, 12);

        foreach (var q in scenario.Questions)
        {
            AddQuestionHeader(body, q.OrderIndex, q.Title, q.Score);
            AddParagraph(body, q.Instruction, false, JustificationValues.Left, 11);

            // If action is ordering CLS services -> render services table
            if (q.ActionCode.Contains("CLS", StringComparison.OrdinalIgnoreCase) && scenario.OrderedServices.Count > 0)
            {
                var svcTable = CreateSubTable(["STT", "Mã Dịch vụ", "Tên Dịch vụ Kỹ thuật / CLS", "Nhóm Dịch vụ"]);
                for (var i = 0; i < scenario.OrderedServices.Count; i++)
                {
                    var s = scenario.OrderedServices[i];
                    AddSubTableRow(svcTable, [(i + 1).ToString(), s.Code, s.Name, s.GroupName]);
                }
                body.Append(svcTable);
            }

            // If action is ordering drugs -> render drugs table
            if (q.ActionCode.Contains("THUOC", StringComparison.OrdinalIgnoreCase) && scenario.OrderedDrugs.Count > 0)
            {
                var drugTable = CreateSubTable(["STT", "Tên Thuốc / Hàm lượng", "ĐVT", "Số lượng", "Cách dùng / Đường dùng"]);
                for (var i = 0; i < scenario.OrderedDrugs.Count; i++)
                {
                    var d = scenario.OrderedDrugs[i];
                    AddSubTableRow(drugTable, [(i + 1).ToString(), d.Name, d.Unit, d.Quantity.ToString(), d.UsageInstructions]);
                }
                body.Append(drugTable);
            }

            // If action is changing service
            if (q.ActionCode.Contains("DOI", StringComparison.OrdinalIgnoreCase) && scenario.ServiceChange is not null)
            {
                AddParagraph(body, $"- Dịch vụ cần hủy/đổi: [{scenario.ServiceChange.CanceledService.Code}] {scenario.ServiceChange.CanceledService.Name}", true, size: 10);
                AddParagraph(body, $"- Dịch vụ mới bổ sung: [{scenario.ServiceChange.NewService.Code}] {scenario.ServiceChange.NewService.Name}", true, size: 10);
            }

            // If action is returning drug
            if (q.ActionCode.Contains("TRA_THUOC", StringComparison.OrdinalIgnoreCase) && scenario.DrugReturn is not null)
            {
                AddParagraph(body, $"- Mặt hàng hoàn trả: {scenario.DrugReturn.ReturnedDrug.Name} (Số lượng trả: {scenario.DrugReturn.ReturnQuantity} {scenario.DrugReturn.ReturnedDrug.Unit})", true, size: 10);
                AddParagraph(body, $"- Lý do hoàn trả: {scenario.DrugReturn.Reason}", false, size: 10);
            }

            AddParagraph(body, "Điểm đạt được: ................................................................................................................", false, size: 10);
            AddParagraph(body, "", false, size: 4);
        }

        // Summary & Signature
        AddParagraph(body, "", false, size: 6);
        AddParagraph(body, $"TỔNG ĐIỂM BÀI THI: {scenario.TotalScore:0.0} ĐIỂM", true, JustificationValues.Right, 12);
        AddParagraph(body, "", false, size: 10);

        var sigTable = CreateBorderLessTable();
        var row = new TableRow();
        var cell1 = new TableCell();
        AddParagraphToCell(cell1, "CÁN BỘ CHẤM THI 1\n(Ký và ghi rõ họ tên)", true, JustificationValues.Center, 11);
        var cell2 = new TableCell();
        AddParagraphToCell(cell2, "CÁN BỘ CHẤM THI 2\n(Ký và ghi rõ họ tên)", true, JustificationValues.Center, 11);
        var cell3 = new TableCell();
        AddParagraphToCell(cell3, "THÍ SINH XÁC NHẬN\n(Ký và ghi rõ họ tên)", true, JustificationValues.Center, 11);
        row.Append(cell1, cell2, cell3);
        sigTable.Append(row);
        body.Append(sigTable);

        body.Append(sectionProps);
        mainPart.Document.Save();
    }

    private static Table CreateStyledTable()
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CBD5E1" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CBD5E1" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "CBD5E1" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "CBD5E1" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }
            )
        );
        table.AppendChild(tableProps);
        return table;
    }

    private static Table CreateSubTable(IReadOnlyList<string> headers)
    {
        var table = CreateStyledTable();
        var headerRow = new TableRow();
        foreach (var h in headers)
        {
            var cell = new TableCell(new TableCellProperties(new Shading { Fill = "F1F5F9", Val = ShadingPatternValues.Clear }));
            AddParagraphToCell(cell, h, true, JustificationValues.Center, 10);
            headerRow.Append(cell);
        }
        table.Append(headerRow);
        return table;
    }

    private static void AddSubTableRow(Table table, IReadOnlyList<string> cells)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = new TableCell();
            var align = i == 0 ? JustificationValues.Center : (i == 3 ? JustificationValues.Right : JustificationValues.Left);
            AddParagraphToCell(cell, cells[i], false, align, 10);
            row.Append(cell);
        }
        table.Append(row);
    }

    private static Table CreateBorderLessTable()
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
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
        return table;
    }

    private static void AddTableRow(Table table, string l1, string v1, string l2, string v2)
    {
        var row = new TableRow();
        var c1 = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1200" }, new Shading { Fill = "F8FAFC", Val = ShadingPatternValues.Clear }));
        AddParagraphToCell(c1, l1, true, JustificationValues.Left, 10);
        var c2 = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1300" }));
        AddParagraphToCell(c2, v1, false, JustificationValues.Left, 10);
        var c3 = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1200" }, new Shading { Fill = "F8FAFC", Val = ShadingPatternValues.Clear }));
        AddParagraphToCell(c3, l2, true, JustificationValues.Left, 10);
        var c4 = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1300" }));
        AddParagraphToCell(c4, v2, false, JustificationValues.Left, 10);
        row.Append(c1, c2, c3, c4);
        table.Append(row);
    }

    private static void AddQuestionHeader(Body body, int index, string title, double score)
    {
        var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "160", After = "60" }));
        var runNum = new Run(
            new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = "22" }, new Bold(), new Color { Val = "0F172A" }),
            new Text($"Câu {index} ({score:0.0} điểm): ")
        );
        var runTitle = new Run(
            new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = "22" }, new Bold(), new Color { Val = "0369A1" }),
            new Text(title)
        );
        p.Append(runNum, runTitle);
        body.Append(p);
    }

    private static void AddParagraphToCell(TableCell cell, string text, bool bold, JustificationValues alignment, int size)
    {
        var lines = (text ?? "").Split('\n');
        foreach (var line in lines)
        {
            var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }, new Justification { Val = alignment }));
            var r = new Run(
                new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = (size * 2).ToString() }),
                new Text(line) { Space = SpaceProcessingModeValues.Preserve }
            );
            if (bold) r.RunProperties!.Append(new Bold());
            p.Append(r);
            cell.Append(p);
        }
    }

    private static void AddParagraph(Body body, string text, bool bold, JustificationValues? alignment = null, int size = 11)
    {
        var p = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "80" }));
        if (alignment is not null) p.ParagraphProperties!.Append(new Justification { Val = alignment.Value });
        var runProps = new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = (size * 2).ToString() });
        if (bold) runProps.Append(new Bold());
        p.Append(new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        body.Append(p);
    }
}
