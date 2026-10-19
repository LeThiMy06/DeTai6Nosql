using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using OnlineSurvey.Models;

namespace OnlineSurvey.Services;

public static class LargeDataSeeder
{
    private static readonly string[] SampleNames =
    [
        "Nguyễn Văn An", "Trần Thị Bích", "Lê Hoàng Cường", "Phạm Minh Đức", "Hoàng Thuỳ Dung",
        "Vũ Quốc Đạt", "Đặng Thảo My", "Bùi Quang Huy", "Đỗ Mai Hương", "Ngô Thành Nam",
        "Dương Quỳnh Như", "Lý Gia Bảo", "Hồ Bảo Ngọc", "Võ Tuấn Kiệt", "Trịnh Phương Linh",
        "Phan Hữu Thắng", "Đinh Tuyết Mai", "Mai Trọng Trí", "Lâm Mỹ Duyên", "Cao Đình Trọng"
    ];

    private static readonly string[] SampleFeedbackSnippets =
    [
        "Hệ thống vận hành mượt mà, câu hỏi rõ ràng và tính ứng dụng cao.",
        "Cần bổ sung thêm tài liệu hướng dẫn và video mô phỏng thực tế.",
        "Rất ấn tượng với quy mô và chất lượng câu hỏi của khảo sát đợt này.",
        "Mong muốn ban tổ chức mở thêm các buổi trao đổi trực tiếp để giải đáp thắc mắc.",
        "Tiến độ phản hồi và chất lượng dịch vụ rất nhanh chóng, chuyên nghiệp.",
        "Đề xuất tối ưu hóa thêm cho các thiết bị di động có màn hình nhỏ.",
        "Nội dung rất thực tế, bao quát trọn vẹn các khía cạnh cần đánh giá.",
        "Nên tổ chức định kỳ mỗi quý hoặc mỗi năm để theo dõi sự cải thiện.",
        "Khảo sát rất chi tiết, phản ánh đúng thực trạng hiện nay.",
        "Hoàn toàn ủng hộ và mong muốn tham gia các giai đoạn phát triển tiếp theo."
    ];

    public static readonly string[] AllLargeSurveySlugs =
    [
        "khao-sat-toan-dien-100-cau-hoi",
        "khao-sat-thi-truong-thiet-bi-cong-nghe-100",
        "khao-sat-van-hoa-doanh-nghiep-it-100",
        "khao-sat-an-toan-thong-tin-chuyen-doi-so-100",
        "khao-sat-dich-vu-y-te-suc-khoe-100"
    ];

    // Slugs cũ cần dọn dẹp nếu có
    private static readonly string[] OldSlugsToClean =
    [
        "khao-sat-thi-truong-thiet-bi-cong-nghe",
        "khao-sat-van-hoa-doanh-nghiep-it",
        "khao-sat-an-toan-thong-tin-chuyen-doi-so",
        "khao-sat-dich-vu-y-te-suc-khoe",
        "khao-sat-khoi-nghiep-doi-moi-sang-tao"
    ];

    public static async Task<(int SurveyCount, int ResponseCount)> SeedAllLargeDataAsync(
        IMongoDatabase database,
        SurveyService? surveyService,
        string adminId)
    {
        if (string.IsNullOrWhiteSpace(adminId))
        {
            var adminCol = database.GetCollection<AdminAccount>("admins");
            var admin = await adminCol.Find(Builders<AdminAccount>.Filter.Empty).FirstOrDefaultAsync();
            adminId = admin?.Id ?? ObjectId.GenerateNewId().ToString();
        }

        var surveysCollection = database.GetCollection<Survey>("surveys");
        var responsesCollection = database.GetCollection<SurveyResponse>("responses");

        // Dọn dẹp cả slug cũ và slug mới để nạp mới hoàn toàn sạch sẽ
        var allSlugs = AllLargeSurveySlugs.Concat(OldSlugsToClean).Distinct().ToArray();
        var oldSurveys = await surveysCollection.Find(s => allSlugs.Contains(s.Slug)).ToListAsync();
        var oldIds = oldSurveys.Select(s => s.Id).ToList();
        if (oldIds.Count > 0)
        {
            await responsesCollection.DeleteManyAsync(r => oldIds.Contains(r.SurveyId));
            await surveysCollection.DeleteManyAsync(s => oldIds.Contains(s.Id));
            if (surveyService is not null && surveyService.IsRedisConnected)
            {
                foreach (var s in oldSurveys)
                {
                    await surveyService.InvalidateAsync(s.Slug);
                }
            }
        }

        var largeSurveys = GenerateAll5SurveysOf100Questions(adminId);
        var totalResponses = 0;

        foreach (var (survey, responseGenCount) in largeSurveys)
        {
            await surveysCollection.InsertOneAsync(survey);

            // Sinh dữ liệu phản hồi mẫu (50 - 60 responses cho mỗi khảo sát 100 câu hỏi)
            var responses = GenerateSampleResponses(survey, responseGenCount);
            if (responses.Count > 0)
            {
                await responsesCollection.InsertManyAsync(responses);
                totalResponses += responses.Count;

                // Đồng bộ sang Redis nếu có SurveyService và Redis kết nối
                if (surveyService is not null && surveyService.IsRedisConnected)
                {
                    foreach (var resp in responses)
                    {
                        var clientKey = SurveyService.HashClient($"{resp.RespondentEmail}_{resp.Id}");
                        await surveyService.IncrementSurveyStatsAsync(survey.Id);
                        await surveyService.TrackUniqueRespondentAsync(survey.Id, clientKey);

                        var eventItem = new
                        {
                            ResponseId = resp.Id,
                            RespondentName = resp.RespondentName,
                            RespondentEmail = resp.RespondentEmail,
                            SubmittedAt = resp.SubmittedAt
                        };
                        await surveyService.PushRecentEventAsync(survey.Id, JsonSerializer.Serialize(eventItem));
                    }
                }
            }
        }

        return (largeSurveys.Count, totalResponses);
    }

    public static List<(Survey Survey, int ResponseCount)> GenerateAll5SurveysOf100Questions(string adminId)
    {
        return
        [
            (BuildSurveyEducation100(adminId), 60),
            (BuildSurveyTechMarket100(adminId), 55),
            (BuildSurveyItWorkplace100(adminId), 55),
            (BuildSurveyCyberSecurity100(adminId), 50),
            (BuildSurveyHealthcare100(adminId), 50)
        ];
    }

    // =========================================================================
    // HÀM TẠO CHUNG: KHẢO SÁT 100 CÂU HỎI (10 CHỦ ĐỀ X 10 CÂU HỎI)
    // =========================================================================
    private static Survey Create100QuestionsSurvey(
        string adminId,
        string slug,
        string title,
        string description,
        int estimatedMinutes,
        int daysAgo,
        (string Category, string[] Prompts, string[] Types)[] topics,
        string[][] multiChoiceOptions)
    {
        var survey = new Survey
        {
            Id = ObjectId.GenerateNewId().ToString(),
            CreatedByAdminId = adminId,
            Slug = slug,
            Title = title,
            Description = description,
            Status = "Published",
            Version = 1,
            EstimatedMinutes = estimatedMinutes,
            CreatedAt = DateTime.UtcNow.AddDays(-daysAgo),
            UpdatedAt = DateTime.UtcNow
        };

        var questions = new List<SurveyQuestion>();
        var qCounter = 1;

        for (int tIdx = 0; tIdx < topics.Length; tIdx++)
        {
            var topic = topics[tIdx];
            for (int pIdx = 0; pIdx < topic.Prompts.Length; pIdx++)
            {
                var qId = $"{slug[..Math.Min(6, slug.Length)]}_q{qCounter}";
                var type = topic.Types[pIdx];
                var prompt = topic.Prompts[pIdx];

                var question = new SurveyQuestion
                {
                    Id = qId,
                    Order = qCounter,
                    Type = type,
                    Text = prompt,
                    Required = false, // Không bắt buộc phải trả lời câu nào trong 100 câu
                    Options = []
                };

                if (type == "single_choice")
                {
                    question.Options =
                    [
                        new() { Id = $"{qId}_opt1", Text = "Rất không hài lòng / Hoàn toàn không đồng ý" },
                        new() { Id = $"{qId}_opt2", Text = "Không hài lòng / Không đồng ý" },
                        new() { Id = $"{qId}_opt3", Text = "Bình thường / Trung lập" },
                        new() { Id = $"{qId}_opt4", Text = "Hài lòng / Đồng ý" },
                        new() { Id = $"{qId}_opt5", Text = "Rất hài lòng / Hoàn toàn đồng ý" }
                    ];
                }
                else if (type == "multiple_choice")
                {
                    var optSet = multiChoiceOptions[tIdx % multiChoiceOptions.Length];
                    question.Options = optSet.Select((txt, idx) => new QuestionOption
                    {
                        Id = $"{qId}_opt{idx + 1}",
                        Text = txt
                    }).ToList();
                }

                questions.Add(question);
                qCounter++;
            }
        }

        survey.Questions = questions;
        return survey;
    }

    // =========================================================================
    // 1. KHẢO SÁT 1: CHẤT LƯỢNG ĐÀO TẠO & SINH VIÊN (100 CÂU HỎI)
    // =========================================================================
    private static Survey BuildSurveyEducation100(string adminId)
    {
        var topics = new (string Category, string[] Prompts, string[] Types)[]
        {
            ("Học vấn & Động lực", [
                "Bạn hiện đang là sinh viên năm thứ mấy?",
                "Ngành học hiện tại có phải là nguyện vọng ưu tiên hàng đầu của bạn?",
                "Bạn dành trung bình bao nhiêu giờ mỗi ngày cho việc tự học ngoài giờ lên lớp?",
                "Mục tiêu học tập chính của bạn trong giai đoạn này là gì?",
                "Bạn đánh giá mức độ nỗ lực tự thân trong học kỳ vừa qua như thế nào?",
                "Phương pháp ghi chép và tự tổng hợp kiến thức bạn thường áp dụng nhất?",
                "Bạn thường xuyên đọc trước bài giảng trước khi đến lớp không?",
                "Động lực lớn nhất thúc đẩy bạn đạt kết quả học tập tốt là gì?",
                "Bạn có dự định học nâng cao (Thạc sĩ/Tiến sĩ hoặc Chứng chỉ quốc tế) không?",
                "Chia sẻ ngắn về kỳ vọng lớn nhất của bạn đối với ngành học này:"
            ], ["single_choice", "single_choice", "single_choice", "multiple_choice", "single_choice", "single_choice", "single_choice", "multiple_choice", "single_choice", "textarea"]),

            ("Nội dung chương trình đào tạo", [
                "Chương trình đào tạo có tính cập nhật công nghệ và xu hướng mới không?",
                "Tỷ lệ giữa lý thuyết và thực hành trong môn học hiện tại đã hợp lý chưa?",
                "Giáo trình và đề cương chi tiết học phần được công khai rõ ràng ngay đầu khóa?",
                "Kiến thức các học phần có tính kế thừa và liên kết logic với nhau?",
                "Mức độ chuyên sâu của các môn chuyên ngành đáp ứng kỳ vọng của bạn?",
                "Khối lượng bài tập và tiểu luận có phù hợp với thời lượng tín chỉ?",
                "Học liệu số (bài giảng điện tử, video record) được cung cấp đầy đủ?",
                "Chương trình đào tạo có trang bị đủ kiến thức để bạn tự học công nghệ mới?",
                "Các môn học kỹ năng nền tảng có thực sự bổ ích cho ngành nghề?",
                "Ý kiến của bạn về việc điều chỉnh khung chương trình đào tạo:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Phương pháp giảng dạy", [
                "Giảng viên có truyền đạt nội dung rõ ràng, dễ hiểu và cuốn hút không?",
                "Giảng viên có khuyến khích sinh viên đặt câu hỏi và phản biện trên lớp?",
                "Giảng viên sử dụng đa dạng công cụ hỗ trợ giảng dạy (slide, demo, video)?",
                "Thời gian phản hồi email hoặc giải đáp thắc mắc ngoài giờ của thầy cô?",
                "Giảng viên có liên hệ kiến thức lý thuyết với bài toán thực tế doanh nghiệp?",
                "Hình thức kiểm tra đánh giá quá trình có phản ánh đúng năng lực?",
                "Việc chấm bài và trả điểm quá trình diễn ra kịp thời và minh bạch?",
                "Mức độ sẵn sàng hỗ trợ nghiên cứu khoa học từ đội ngũ giảng viên?",
                "Bạn đánh giá mức độ chuyên nghiệp và tâm huyết chung của giảng viên?",
                "Đề xuất của bạn nhằm nâng cao chất lượng giảng dạy trên lớp:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Năng lực số & Ứng dụng AI", [
                "Tần suất bạn sử dụng các công cụ Trí tuệ nhân tạo (ChatGPT, Claude, Gemini) để học?",
                "Bạn đã ứng dụng AI vào các hoạt động học tập nào?",
                "Nhà trường đã có quy định và hướng dẫn sử dụng AI có đạo đức chưa?",
                "Mức độ thành thạo của bạn với các hệ quản trị CSDL NoSQL (MongoDB, Redis)?",
                "Bạn tự đánh giá kỹ năng lập trình và làm việc với Git/GitHub ở mức nào?",
                "Hệ thống quản lý học tập (LMS/Portal) của trường hoạt động ổn định không?",
                "Bạn có thường xuyên tham gia các khóa học trực tuyến (Coursera, Udemy)?",
                "Bạn đánh giá kỹ năng bảo mật thông tin và an toàn tài khoản cá nhân?",
                "Năng lực giải quyết vấn đề bằng tư duy thuật toán của bạn thế nào?",
                "Bạn mong muốn nhà trường bổ sung các workshop công nghệ nào nhất?"
            ], ["single_choice", "multiple_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Cơ sở vật chất", [
                "Hệ thống điều hòa và ánh sáng tại các phòng học lý thuyết?",
                "Cấu hình và tốc độ xử lý của máy tính tại các phòng thực hành?",
                "Hạ tầng Wi-Fi tại khuôn viên trường có đảm bảo kết nối ổn định?",
                "Hệ thống máy chiếu, micro và âm thanh phòng học hoạt động tốt?",
                "Mức độ vệ sinh của các khu vực vệ sinh chung trong trường?",
                "Không gian tự học và làm việc nhóm dành cho sinh viên?",
                "Khu vực gửi xe có an toàn, trật tự và quy trình lấy xe nhanh chóng?",
                "Mức độ tiện nghi của căng tin và chất lượng an toàn thực phẩm?",
                "Không gian cây xanh và cảnh quan môi trường sư phạm tổng thể?",
                "Đề xuất ưu tiên nâng cấp hạng mục cơ sở vật chất nào trước:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Thư viện & Nghiên cứu khoa học", [
                "Số lượng đầu sách in và giáo trình chuyên ngành tại thư viện?",
                "Khả năng truy cập cơ sở dữ liệu số và bài báo quốc tế (IEEE, ScienceDirect)?",
                "Không gian yên tĩnh và thời gian mở cửa của thư viện đáp ứng nhu cầu?",
                "Bạn đã từng tham gia đề tài Nghiên cứu khoa học sinh viên chưa?",
                "Chính sách hỗ trợ kinh phí nghiên cứu khoa học sinh viên của trường?",
                "Mức độ hướng dẫn phương pháp nghiên cứu từ người hướng dẫn?",
                "Bạn đánh giá cơ hội công bố bài báo khoa học tại trường như thế nào?",
                "Nguồn tài liệu tham khảo nào hỗ trợ bạn đắc lực nhất?",
                "Thái độ phục vụ của cán bộ thư viện và hỗ trợ học liệu?",
                "Góp ý của bạn để thư viện phục vụ tốt hơn cho sinh viên:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "multiple_choice", "single_choice", "textarea"]),

            ("Kỹ năng mềm & Ngoại ngữ", [
                "Mức độ tự tin trong kỹ năng thuyết trình trước đám đông của bạn?",
                "Khả năng phối hợp và giải quyết xung đột khi làm việc nhóm?",
                "Khả năng đọc hiểu tài liệu chuyên ngành bằng Tiếng Anh?",
                "Bạn tự tin với kỹ năng giao tiếp tiếng Anh (nghe - nói) ở mức nào?",
                "Khả năng quản lý thời gian và áp lực mùa thi của bạn?",
                "Nhà trường đã trang bị đủ các khóa đào tạo kỹ năng mềm cần thiết?",
                "Tư duy phản biện và giải quyết vấn đề phức tạp của bạn?",
                "Bạn có hay đảm nhận vai trò nhóm trưởng (Team Leader) trong bài tập lớn?",
                "Những kỹ năng mềm bạn cảm thấy bản thân còn yếu nhất hiện nay?",
                "Đề xuất câu lạc bộ hoặc workshop kỹ năng bạn muốn tham gia:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "multiple_choice", "text"]),

            ("Thực tập & Hướng nghiệp", [
                "Nhà trường có kết nối mạng lưới doanh nghiệp thực tập uy tín?",
                "Ngày hội việc làm (Job Fair) tại trường có nhiều cơ hội chất lượng?",
                "Thời lượng kỳ thực tập doanh nghiệp hiện tại có đủ dài?",
                "Bạn đã chuẩn bị CV chuyên nghiệp và hồ sơ LinkedIn/Portfolio chưa?",
                "Mức độ tự tin ứng tuyển vị trí thực tập / Fresher tại doanh nghiệp?",
                "Bạn đánh giá sự hỗ trợ của giảng viên hướng dẫn thực tập như thế nào?",
                "Doanh nghiệp thực tập có đánh giá cao sinh viên của trường?",
                "Yếu tố bạn coi trọng nhất khi chọn công ty thực tập?",
                "Bạn có mong muốn được nhà trường tổ chức phỏng vấn thử (Mock Interview)?",
                "Mong muốn hỗ trợ gì nhất từ Trung tâm Quan hệ Doanh nghiệp:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "multiple_choice", "single_choice", "textarea"]),

            ("Đời sống & Sức khỏe tinh thần", [
                "Số lượng và chất lượng các hoạt động Đoàn - Hội - CLB sinh viên?",
                "Mức độ hỗ trợ về đời sống tinh thần và tư vấn tâm lý học đường?",
                "Các sân chơi thể thao, rèn luyện thể chất trong trường có đầy đủ?",
                "Bạn có gặp áp lực căng thẳng (stress) kéo dài trong quá trình học?",
                "Điều kiện sinh hoạt tại ký túc xá hoặc khu vực trọ xung quanh trường?",
                "Mức độ an ninh trật tự trong khuôn viên trường học?",
                "Các hoạt động tình nguyện vì cộng đồng có thu hút bạn tham gia?",
                "Phương thức bạn thường dùng để giải tỏa áp lực học tập?",
                "Bạn có thấy gắn kết và tự hào với ngôi trường mình đang học?",
                "Đề xuất cho các hoạt động ngoại khóa học kỳ tới:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "multiple_choice", "single_choice", "text"]),

            ("Dịch vụ hành chính & Cải tiến", [
                "Thủ tục hành chính xin bảng điểm, giấy xác nhận sinh viên nhanh gọn?",
                "Thái độ phục vụ của chuyên viên tại phòng Đào tạo và Công tác sinh viên?",
                "Kênh tiếp nhận phản ánh, khiếu nại của sinh viên có phản hồi thỏa đáng?",
                "Hệ thống đăng ký môn học trực tuyến có xử lý mượt mà vào giờ cao điểm?",
                "Thông báo học phí và các chính sách học bổng được công khai minh bạch?",
                "Tỷ lệ sinh viên nhận học bổng khuyến khích học tập có thỏa đáng?",
                "Ứng dụng di động hoặc cổng thông tin sinh viên có tiện lợi?",
                "Mức độ hài lòng tổng thể của bạn đối với toàn bộ dịch vụ của nhà trường?",
                "Bạn có sẵn lòng giới thiệu trường cho người thân, bạn bè không?",
                "Đóng góp ý kiến tâm huyết nhất của bạn để xây dựng nhà trường ngày càng phát triển:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"])
        };

        var multiOpts = new[]
        {
            new[] { "Học bổng khuyến khích học tập", "Cơ hội việc làm lương cao", "Đam mê nghiên cứu khoa học", "Sự kỳ vọng của gia đình", "Khát khao phát triển bản thân" },
            new[] { "Giải quyết bài tập & Code demo", "Tóm tắt giáo trình chuyên ngành", "Luyện viết ngoại ngữ", "Lên ý tưởng đề tài đồ án", "Tối ưu hóa học nhóm" },
            new[] { "Kỹ năng giao tiếp & Thuyết trình", "Quản lý thời gian & Áp lực", "Lãnh đạo & Giải quyết xung đột", "Tư duy phản biện & Phân tích", "Đàm phán & Ngoại giao" },
            new[] { "Chế độ đãi ngộ & Trợ cấp thực tập", "Môi trường học hỏi từ Mentor", "Cơ hội lên nhân viên chính thức", "Thương hiệu doanh nghiệp lớn", "Thời gian làm việc linh hoạt" }
        };

        return Create100QuestionsSurvey(
            adminId,
            "khao-sat-toan-dien-100-cau-hoi",
            "Khảo sát toàn diện chất lượng đào tạo và năng lực sinh viên (100 câu hỏi)",
            "Bộ khảo sát quy mô quốc gia gồm 100 câu hỏi đánh giá toàn diện 10 lĩnh vực: học thuật, giảng dạy, kỹ năng số & AI, cơ sở vật chất, thư viện, kỹ năng mềm, thực tập doanh nghiệp, đời sống học đường và dịch vụ sinh viên.",
            25,
            7,
            topics,
            multiOpts);
    }

    // =========================================================================
    // 2. KHẢO SÁT 2: THỊ TRƯỜNG THIẾT BỊ CÔNG NGHỆ & AI (100 CÂU HỎI)
    // =========================================================================
    private static Survey BuildSurveyTechMarket100(string adminId)
    {
        var topics = new (string Category, string[] Prompts, string[] Types)[]
        {
            ("Smartphone & Trải nghiệm di động", [
                "Bạn hiện đang sử dụng điện thoại thông minh thuộc phân khúc giá nào?",
                "Thời gian bạn nâng cấp thiết bị điện thoại định kỳ là bao lâu một lần?",
                "Hệ điều hành di động bạn đang sử dụng chính hiện nay?",
                "Mức độ hài lòng về thời lượng pin và tốc độ sạc nhanh trên máy bạn?",
                "Bạn đánh giá thế nào về chất lượng camera chụp ảnh đêm và quay video?",
                "Tần số quét màn hình 120Hz có ảnh hưởng lớn đến quyết định chọn mua?",
                "Khả năng kháng nước và kháng bụi chuẩn IP68 có thực sự cần thiết?",
                "Mức độ quan tâm của bạn đến thiết kế mặt lưng và vật liệu (titan, gốm)?",
                "Tính năng sạc không dây và sạc ngược không dây bạn có hay dùng?",
                "Chia sẻ điều bạn chưa hài lòng nhất trên chiếc điện thoại hiện tại:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Laptop & Máy tính cá nhân", [
                "Hệ điều hành máy tính bạn thường xuyên làm việc nhất?",
                "Dòng chip xử lý (CPU) trên máy tính hiện tại của bạn?",
                "Dung lượng RAM tối thiểu bạn thấy cần thiết cho công việc hiện nay?",
                "Thời lượng pin thực tế của laptop có đáp ứng trọn vẹn một ngày làm việc?",
                "Độ mỏng nhẹ và tính cơ động của máy tính quan trọng ra sao với bạn?",
                "Chất lượng bàn phím và bàn di chuột (trackpad) trên máy tính của bạn?",
                "Màn hình có độ chuẩn màu cao (100% sRGB / DCI-P3) có quan trọng?",
                "Khả năng tản nhiệt và độ ồn của quạt khi máy hoạt động tải nặng?",
                "Số lượng cổng kết nối (USB-C, Thunderbolt, HDMI) trên máy có đủ dùng?",
                "Bạn mong muốn dòng laptop tương lai cải tiến điều gì nhất:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Thiết bị đeo & Chăm sóc sức khỏe", [
                "Bạn có đang sử dụng đồng hồ thông minh (Smartwatch) hoặc vòng đeo tay?",
                "Tính năng theo dõi sức khỏe nào bạn sử dụng thường xuyên nhất?",
                "Độ chính xác của cảm biến đo nhịp tim và nồng độ oxy trong máu (SpO2)?",
                "Thời lượng pin của thiết bị đeo có khiến bạn cảm thấy hài lòng?",
                "Khả năng theo dõi giấc ngủ và nhắc nhở vận động có giúp ích cho bạn?",
                "Tính năng nhận cuộc gọi và trả lời tin nhắn nhanh trên đồng hồ?",
                "Mức độ quan tâm của bạn đối với sản phẩm Nhẫn thông minh (Smart Ring)?",
                "Khả năng chống nước khi bơi lội của thiết bị đeo hoạt động tốt không?",
                "Thiết kế thời trang và khả năng thay đổi dây đeo có quan trọng?",
                "Đề xuất tính năng sức khỏe bạn mong muốn xuất hiện trên thiết bị đeo:"
            ], ["single_choice", "multiple_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Âm thanh & Phụ kiện thông minh", [
                "Bạn thường sử dụng loại tai nghe nào nhiều nhất hàng ngày?",
                "Tính năng chống ồn chủ động (ANC) có thực sự hiệu quả khi đi đường?",
                "Thời lượng pin hộp sạc tai nghe không dây đáp ứng tốt nhu cầu?",
                "Chất lượng đàm thoại và lọc gió của micro tai nghe khi gọi điện?",
                "Mức độ trung thực và âm bass của các dòng tai nghe hiện tại?",
                "Bạn có sở hữu loa thông minh kết nối trợ lý ảo trong nhà không?",
                "Các loại củ sạc nhanh công nghệ GaN có phải ưu tiên chọn mua?",
                "Bạn đánh giá thế nào về chất lượng cáp sạc bọc dù chống đứt?",
                "Mức độ tin cậy của các loại sạc dự phòng dung lượng lớn hiện nay?",
                "Nhận xét về hệ sinh thái phụ kiện công nghệ bạn đang dùng:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Nhà thông minh (Smart Home) & IoT", [
                "Gia đình bạn đã lắp đặt bao nhiêu thiết bị thông minh trong nhà?",
                "Những thiết bị Smart Home nào bạn đang sử dụng phổ biến nhất?",
                "Mức độ ổn định của kết nối Wi-Fi giữa các thiết bị gia dụng IoT?",
                "Ứng dụng điều khiển nhà thông minh trên điện thoại có dễ thao tác?",
                "Khả năng tự động hóa theo kịch bản (ra khỏi nhà, đi ngủ) hoạt động tốt?",
                "Khóa cửa thông minh nhận diện vân tay/khuôn mặt có an toàn?",
                "Hệ thống camera an ninh giám sát gia đình có rõ nét vào ban đêm?",
                "Robot hút bụi lau nhà thông minh có giải phóng sức lao động đáng kể?",
                "Chi phí đầu tư hệ sinh thái nhà thông minh có hợp lý hiện nay?",
                "Rào cản lớn nhất khiến bạn chưa mở rộng thêm thiết bị nhà thông minh:"
            ], ["single_choice", "multiple_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("AI tạo sinh & Trợ lý thông minh", [
                "Tần suất bạn sử dụng các trợ lý ảo tích hợp AI (Siri, Google, Copilot)?",
                "Bạn đánh giá thế nào về khả năng dịch trực tiếp cuộc gọi bằng AI?",
                "Tính năng tóm tắt văn bản và ghi âm cuộc họp bằng AI có hữu ích?",
                "Khả năng chỉnh sửa và xóa vật thể thông minh trong ảnh bằng AI?",
                "Bạn có sẵn lòng trả phí thuê bao hàng tháng cho các gói tính năng AI cao cấp?",
                "Mức độ hiểu ngữ cảnh tiếng Việt của các mô hình AI hiện nay?",
                "Tính năng tự động tạo email hoặc trả lời tin nhắn có đáng tin cậy?",
                "Bạn có lo ngại về việc thiết bị xử lý AI trên đám mây làm lộ bí mật?",
                "Khả năng xử lý AI trực tiếp trên chip thiết bị (On-device NPU)?",
                "Kỳ vọng lớn nhất của bạn về trợ lý AI trên thiết bị cá nhân:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Hành vi tiêu dùng & Ngân sách", [
                "Ngân sách trung bình hàng năm bạn dành cho việc mua sắm đồ công nghệ?",
                "Yếu tố nào quyết định lớn nhất khi bạn quyết định mua một sản phẩm mới?",
                "Bạn có thường xuyên mua thiết bị công nghệ theo hình thức trả góp 0%?",
                "Chính sách thu cũ đổi mới (Trade-in) có hấp dẫn bạn nâng cấp máy?",
                "Bạn thường tìm hiểu thông tin đánh giá sản phẩm từ nguồn nào nhất?",
                "Các chương trình khuyến mãi giảm giá (Mega Sale) có kích thích bạn mua?",
                "Bạn có thói quen mua thêm gói bảo hiểm rơi vỡ / vào nước không?",
                "Thời gian bạn cân nhắc từ khi thích sản phẩm đến khi quyết định mua?",
                "Thương hiệu công nghệ nào bạn cảm thấy có độ giữ giá tốt nhất?",
                "Chia sẻ kinh nghiệm mua sắm đồ công nghệ tiết kiệm và thông thái:"
            ], ["single_choice", "multiple_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Kênh phân phối & Dịch vụ bảo hành", [
                "Bạn thường mua thiết bị công nghệ tại hệ thống bán lẻ hay sàn TMĐT?",
                "Trải nghiệm tiếp đón và tư vấn của nhân viên tại các cửa hàng công nghệ?",
                "Thời gian xử lý bảo hành hoặc đổi trả sản phẩm lỗi có nhanh gọn?",
                "Thái độ phục vụ của trung tâm bảo hành chính hãng tại Việt Nam?",
                "Bạn đánh giá thế nào về chính sách 1 đổi 1 trong 30 ngày đầu?",
                "Việc tra cứu thông tin bảo hành điện tử (e-Warranty) có thuận tiện?",
                "Chất lượng dịch vụ giao hàng hỏa tốc khi đặt hàng online?",
                "Chính sách bảo hành toàn cầu hoặc hỗ trợ sửa chữa lưu động?",
                "Mức độ uy tín của các hệ thống bán lẻ công nghệ lớn hiện nay?",
                "Góp ý để cải thiện dịch vụ hậu mãi và chăm sóc khách hàng công nghệ:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Quyền riêng tư & An toàn dữ liệu", [
                "Mức độ lo ngại của bạn về việc ứng dụng điện thoại nghe lén quảng cáo?",
                "Bạn có thường xuyên đọc điều khoản quyền riêng tư khi cài ứng dụng mới?",
                "Thói quen tắt quyền truy cập vị trí và micro khi không sử dụng?",
                "Mức độ tin cậy của phương thức bảo mật sinh trắc học (vân tay, Face ID)?",
                "Bạn có sử dụng ứng dụng quản lý mật khẩu (Password Manager) không?",
                "Tần suất bạn sao lưu dữ liệu quan trọng lên iCloud / Google Drive / OneDrive?",
                "Bạn đánh giá tính năng phát hiện phần mềm độc hại và chặn link lừa đảo?",
                "Cảnh báo theo dõi vị trí từ các thiết bị định vị (AirTag, SmartTag)?",
                "Mức độ an tâm khi kết nối Wi-Fi công cộng tại quán cà phê, sân bay?",
                "Đề xuất của bạn về chính sách bảo vệ dữ liệu người dùng của các hãng:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Xu hướng tương lai & Đổi mới sáng tạo", [
                "Mức độ quan tâm của bạn đến kính thực tế ảo / thực tế tăng cường (VR/AR)?",
                "Bạn có tin rằng xe điện thông minh sẽ thay thế hoàn toàn xe xăng?",
                "Xu hướng màn hình gập ba (Tri-fold) có tiềm năng trở thành xu thế mới?",
                "Khả năng kết nối vệ tinh trực tiếp trên điện thoại khi mất sóng di động?",
                "Mức độ ủng hộ của bạn đối với việc hãng bỏ củ sạc để bảo vệ môi trường?",
                "Công nghệ pin thể rắn giúp tăng gấp đôi dung lượng có đáng chờ đợi?",
                "Khả năng robot hình người (Humanoid Robot) hỗ trợ công việc gia đình?",
                "Mức độ sẵn sàng đón nhận thiết bị công nghệ điều khiển bằng sóng não?",
                "Bạn đánh giá tốc độ đổi mới sáng tạo của ngành công nghệ trong 5 năm qua?",
                "Dự đoán của bạn về công nghệ đột phá nhất sẽ làm thay đổi cuộc sống:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"])
        };

        var multiOpts = new[]
        {
            new[] { "Đo nhịp tim liên tục 24/7", "Đo nồng độ oxy trong máu (SpO2)", "Theo dõi chất lượng giấc ngủ", "Đo điện tâm đồ (ECG)", "Theo dõi chỉ số căng thẳng (Stress)" },
            new[] { "Bóng đèn & Đèn LED thông minh", "Camera an ninh giám sát", "Khóa cửa vân tay thông minh", "Robot hút bụi lau nhà", "Ổ cắm & Công tắc thông minh" },
            new[] { "Hiệu năng chip & Dung lượng bộ nhớ", "Thời lượng pin & Tốc độ sạc", "Chất lượng màn hình hiển thị", "Hệ thống camera & Quay video", "Mức giá & Khuyến mãi đi kèm" },
            new[] { "Kênh YouTube review công nghệ", "Nhóm cộng đồng mạng xã hội", "Bài viết trang tin tức công nghệ", "Trải nghiệm trực tiếp tại cửa hàng", "Lời khuyên từ bạn bè, đồng nghiệp" }
        };

        return Create100QuestionsSurvey(
            adminId,
            "khao-sat-thi-truong-thiet-bi-cong-nghe-100",
            "Khảo sát toàn diện thị trường thiết bị thông minh, hệ sinh thái AI và người tiêu dùng (100 câu hỏi)",
            "Nghiên cứu thị trường quy mô lớn gồm 100 câu hỏi chuyên sâu về thói quen lựa chọn smartphone, laptop, smartwatch, thiết bị âm thanh, smart home, ứng dụng AI và dịch vụ hậu mãi.",
            25,
            10,
            topics,
            multiOpts);
    }

    // =========================================================================
    // 3. KHẢO SÁT 3: VĂN HÓA DOANH NGHIỆP & MÔI TRƯỜNG CNTT (100 CÂU HỎI)
    // =========================================================================
    private static Survey BuildSurveyItWorkplace100(string adminId)
    {
        var topics = new (string Category, string[] Prompts, string[] Types)[]
        {
            ("Thông tin nghề nghiệp & Vị trí", [
                "Số năm kinh nghiệm làm việc thực tế trong lĩnh vực CNTT/Phần mềm?",
                "Vị trí công tác chính hiện tại của bạn tại tổ chức?",
                "Quy mô nhân sự của công ty / tổ chức bạn đang làm việc?",
                "Hình thức doanh nghiệp của bạn (Outsourcing, Product, Startup)?",
                "Ngôn ngữ lập trình hoặc công nghệ bạn sử dụng nhiều nhất hàng ngày?",
                "Số lượng dự án phần mềm bạn đã từng trực tiếp tham gia hoàn thiện?",
                "Bạn có thường xuyên làm việc trực tiếp với khách hàng quốc tế không?",
                "Lý do quan trọng nhất khiến bạn theo đuổi sự nghiệp ngành CNTT?",
                "Bạn đánh giá năng lực thích ứng với công nghệ mới của bản thân ra sao?",
                "Mô tả ngắn về mục tiêu nghề nghiệp lớn nhất của bạn trong 3 năm tới:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "multiple_choice", "single_choice", "single_choice", "multiple_choice", "single_choice", "textarea"]),

            ("Lương bổng & Phúc lợi tài chính", [
                "Mức độ hài lòng chung về thu nhập và lương cơ bản hiện tại của bạn?",
                "Công ty có chính sách xét duyệt tăng lương định kỳ minh bạch không?",
                "Mức thưởng hiệu quả công việc và thưởng cuối năm (tháng 13, KPI)?",
                "Chính sách thưởng dự án hoặc chia sẻ lợi nhuận (ESOP) có hấp dẫn?",
                "Mức phụ cấp ăn trưa, gửi xe và thiết bị làm việc có thỏa đáng?",
                "Chế độ bảo hiểm sức khỏe bổ sung (Bảo Việt, PTI) cho cá nhân và người thân?",
                "Chính sách hỗ trợ kinh phí thi chứng chỉ quốc tế (AWS, Azure, PMP)?",
                "Chế độ nghỉ phép năm (từ 12 đến 16 ngày) có được tôn trọng đầy đủ?",
                "Công ty có chính sách cho vay ưu đãi hoặc hỗ trợ tài chính đột xuất?",
                "Ý kiến của bạn về việc tối ưu cơ chế đãi ngộ của doanh nghiệp:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Thời gian làm việc & Mô hình linh hoạt", [
                "Mô hình làm việc hiện tại của bạn (On-site, Remote, Hybrid)?",
                "Mức độ tự chủ và linh hoạt trong việc sắp xếp khung giờ làm việc?",
                "Số giờ làm thêm (OT) trung bình mỗi tuần trong các giai đoạn cao điểm?",
                "Chế độ chấm công và tính tiền làm thêm giờ (OT) có đúng quy định?",
                "Làm việc từ xa có giúp bạn tiết kiệm thời gian và nâng cao năng suất?",
                "Công ty có tôn trọng thời gian cá nhân sau giờ làm việc (không nhắn việc đêm)?",
                "Mức độ dễ dàng khi xin nghỉ phép đột xuất vì lý do gia đình hoặc sức khỏe?",
                "Công ty có hỗ trợ chi phí điện, mạng internet khi làm việc tại nhà?",
                "Bạn cảm thấy mô hình làm việc nào mang lại hiệu quả công việc cao nhất?",
                "Đề xuất của bạn về chính sách thời gian làm việc linh hoạt:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Văn hóa doanh nghiệp & Môi trường", [
                "Không khí làm việc tại văn phòng có thân thiện và cởi mở không?",
                "Mức độ tôn trọng sự đa dạng, bình đẳng và không phân biệt đối xử?",
                "Nhân viên có được tự do bày tỏ quan điểm và phản biện mang tính xây dựng?",
                "Mức độ minh bạch trong các quyết định điều hành từ Ban giám đốc?",
                "Văn hóa ghi nhận và khen thưởng cá nhân có đóng góp xuất sắc?",
                "Mức độ gắn kết và tinh thần hỗ trợ lẫn nhau giữa các thành viên?",
                "Công ty có giải quyết thỏa đáng khi xảy ra mâu thuẫn nội bộ?",
                "Các hoạt động teambuilding, dã ngoại và du lịch hàng năm có hấp dẫn?",
                "Văn hóa chia sẻ kiến thức nội bộ (Tech Talk, Workshop) có sôi nổi?",
                "Cảm nhận của bạn về bản sắc văn hóa đặc trưng nhất của công ty:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Lãnh đạo & Quản lý trực tiếp", [
                "Trình độ chuyên môn và năng lực giải quyết vấn đề của Quản lý trực tiếp?",
                "Quản lý có phân chia khối lượng công việc công bằng và rõ ràng không?",
                "Khả năng lắng nghe và đồng cảm của người quản lý khi nhân viên gặp khó?",
                "Tần suất phản hồi 1-on-1 định kỳ giữa bạn và cấp trên trực tiếp?",
                "Lãnh đạo có tạo điều kiện và trao quyền để bạn tự chủ xử lý dự án?",
                "Sự hỗ trợ kịp thời từ cấp quản lý khi dự án gặp sự cố kỹ thuật lớn?",
                "Mức độ truyền cảm hứng và định hướng tầm nhìn dài hạn của lãnh đạo?",
                "Người quản lý có bảo vệ quyền lợi chính đáng của đội ngũ trước cấp trên?",
                "Mức độ tin tưởng và tôn trọng bạn dành cho người quản lý trực tiếp?",
                "Đóng góp ý kiến để cấp quản lý nâng cao kỹ năng lãnh đạo đội ngũ:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Đào tạo, Chứng chỉ & Thăng tiến", [
                "Công ty có lộ trình phát triển nghề nghiệp (Career Ladder) rõ ràng?",
                "Số lượng các khóa đào tạo nội bộ cập nhật công nghệ mới hàng năm?",
                "Cơ hội được thử sức ở các vai trò mới (Tech Lead, Architect, PM)?",
                "Chính sách tài trợ 100% kinh phí thi các chứng chỉ quốc tế uy tín?",
                "Quy trình đánh giá thăng tiến và bổ nhiệm chức danh có công tâm?",
                "Mức độ sẵn sàng chia sẻ kinh nghiệm từ các Senior và Chuyên gia?",
                "Bạn có được định hướng phát triển chuyên môn sâu (Subject Matter Expert)?",
                "Thời gian trung bình để một nhân sự có năng lực được xét thăng cấp?",
                "Công ty có hỗ trợ ngân sách mua sách chuyên ngành và tài khoản học online?",
                "Khóa đào tạo kỹ năng nào bạn mong muốn tham gia nhất trong năm nay:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Quy trình kỹ thuật & Công cụ", [
                "Mức độ áp dụng hiệu quả quy trình phát triển phần mềm Agile/Scrum?",
                "Quy trình kiểm thử chất lượng (QA/QC) và kiểm thử tự động (Automation)?",
                "Mức độ hoàn thiện của hệ thống tích hợp và triển khai liên tục (CI/CD)?",
                "Hệ thống giám sát lỗi ứng dụng và ghi nhật ký (Monitoring/Logging)?",
                "Chính sách rà soát mã nguồn (Code Review) có diễn ra nghiêm túc?",
                "Công cụ quản lý dự án (Jira, Trello, Azure DevOps) có tiện lợi?",
                "Mức độ nợ kỹ thuật (Technical Debt) tại các dự án phần mềm công ty?",
                "Công ty có khuyến khích ứng dụng các công cụ hỗ trợ AI (GitHub Copilot)?",
                "Quy trình bàn giao và tài liệu kỹ thuật (Documentation) có đầy đủ?",
                "Đề xuất cải tiến quy trình kỹ thuật để nâng cao chất lượng phần mềm:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Sức khỏe tinh thần & Cân bằng", [
                "Mức độ cân bằng giữa công việc và cuộc sống cá nhân (Work-Life Balance)?",
                "Bạn có thường xuyên cảm thấy kiệt sức, lo âu (Burnout) vì áp lực dự án?",
                "Công ty có các chương trình tư vấn tâm lý hoặc chăm sóc sức khỏe tinh thần?",
                "Chính sách ngày nghỉ phục hồi năng lượng (Mental Health Day)?",
                "Các câu lạc bộ thể thao (chạy bộ, bóng đá, cầu lông, yoga) tại công ty?",
                "Mức độ thoải mái khi từ chối nhận thêm việc ngoài phạm vi trách nhiệm?",
                "Áp lực từ deadline gấp có khiến chất lượng cuộc sống bị ảnh hưởng nghiêm trọng?",
                "Thời gian dành cho gia đình và sở thích cá nhân vào các ngày cuối tuần?",
                "Bạn đánh giá thế nào về môi trường làm việc không độc hại (Toxic-free)?",
                "Phương thức hiệu quả nhất giúp bạn giải tỏa áp lực công việc hàng ngày:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Cơ sở vật chất văn phòng", [
                "Chất lượng máy tính làm việc (MacBook Pro, ThinkPad, màn hình rời 4K)?",
                "Bàn ghế công thái học (Ergonomic Chair) tại văn phòng có thoải mái?",
                "Tốc độ và độ ổn định của đường truyền internet cáp quang tại văn phòng?",
                "Không gian phòng họp, thiết bị hội nghị truyền hình (Zoom/Teams Rooms)?",
                "Khu vực Pantry, đồ ăn nhẹ, cà phê và trà miễn phí có phong phú?",
                "Mức độ sạch sẽ, thoáng mát và nhiệt độ điều hòa tại không gian chung?",
                "Không gian thư giãn, chơi game (PS5, bi lắc, bóng bàn) giải tỏa căng thẳng?",
                "Vị trí địa lý văn phòng và sự thuận tiện khi di chuyển, gửi xe?",
                "Mức độ cách âm giữa các khu vực làm việc tập trung và thảo luận nhóm?",
                "Đề xuất nâng cấp trang thiết bị hoặc tiện ích văn phòng:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Mức độ gắn bó & Kiến nghị", [
                "Bạn có cảm thấy tự hào khi giới thiệu nơi làm việc của mình với người khác?",
                "Mức độ sẵn sàng giới thiệu bạn bè, đồng nghiệp cũ gia nhập công ty?",
                "Khả năng bạn tiếp tục gắn bó với công ty trong 2 năm tới ở mức nào?",
                "Nếu nhận được lời mời hấp dẫn từ đối thủ, mức độ bạn cân nhắc rời đi?",
                "Bạn có cảm nhận được sự đóng góp của mình vào thành công chung của công ty?",
                "Mức độ tin tưởng vào chiến lược phát triển kinh doanh của Ban lãnh đạo?",
                "Công ty có phải là nơi lý tưởng để bạn phát triển sự nghiệp lâu dài?",
                "Mức độ hạnh phúc tổng thể của bạn trong mỗi ngày đi làm?",
                "Yếu tố then chốt nhất giữ chân bạn ở lại cống hiến cho công ty?",
                "Góp ý tâm huyết nhất để xây dựng doanh nghiệp trở thành Nơi làm việc tốt nhất:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"])
        };

        var multiOpts = new[]
        {
            new[] { "C# / .NET", "Java / Spring Boot", "JavaScript / TypeScript", "Python / AI / Data", "Golang / Rust" },
            new[] { "Lương cơ bản cạnh tranh", "Cơ hội làm việc từ xa (Remote/Hybrid)", "Thưởng dự án & Cổ phần ESOP", "Bảo hiểm sức khỏe cao cấp", "Văn hóa minh bạch và cởi mở" },
            new[] { "Chứng chỉ Cloud (AWS/Azure/GCP)", "Chứng chỉ DevOps & Kubernetes", "Chứng chỉ Quản trị dự án (PMP/Scrum)", "Chứng chỉ An toàn thông tin (CISSP)", "Chứng chỉ AI & Machine Learning" },
            new[] { "Tập thể thao & Chạy bộ", "Nghe nhạc & Chơi game", "Gặp gỡ bạn bè & Gia đình", "Đi du lịch ngắn ngày", "Ngủ đủ giấc & Thiền định" }
        };

        return Create100QuestionsSurvey(
            adminId,
            "khao-sat-van-hoa-doanh-nghiep-it-100",
            "Khảo sát toàn diện văn hóa doanh nghiệp, đãi ngộ và môi trường làm việc CNTT (100 câu hỏi)",
            "Đánh giá toàn diện 10 khía cạnh quan trọng của đời sống nhân sự công nghệ: lương thưởng, mô hình làm việc, văn hóa tôn trọng, năng lực lãnh đạo, lộ trình thăng tiến, quy trình kỹ thuật, cân bằng cuộc sống và gắn kết lâu dài.",
            25,
            12,
            topics,
            multiOpts);
    }

    // =========================================================================
    // 4. KHẢO SÁT 4: AN TOÀN THÔNG TIN & CHUYỂN ĐỔI SỐ (100 CÂU HỎI)
    // =========================================================================
    private static Survey BuildSurveyCyberSecurity100(string adminId)
    {
        var topics = new (string Category, string[] Prompts, string[] Types)[]
        {
            ("Nhận thức an ninh mạng cá nhân", [
                "Bạn tự đánh giá mức độ hiểu biết về an toàn thông tin cơ bản ở mức nào?",
                "Bạn có thói quen khóa màn hình máy tính (Win+L) khi rời khỏi vị trí?",
                "Tần suất bạn tham gia các buổi đào tạo nhận thức an ninh mạng định kỳ?",
                "Bạn có kiểm tra kỹ địa chỉ email người gửi trước khi nhấp vào liên kết lạ?",
                "Thói quen không cắm USB lạ hoặc thiết bị không rõ nguồn gốc vào máy tính?",
                "Bạn có phân loại mức độ bảo mật của dữ liệu (Công khai, Nội bộ, Tuyệt mật)?",
                "Mức độ cảnh giác của bạn trước các tin nhắn mạo danh lãnh đạo cơ quan?",
                "Bạn có đọc kỹ các thông báo cảnh báo mã độc từ bộ phận IT nội bộ?",
                "Mức độ tự giác tuân thủ quy chế an toàn thông tin tại đơn vị?",
                "Theo bạn, khó khăn lớn nhất trong việc duy trì nhận thức an ninh mạng là gì:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Quản lý danh tính & Xác thực (IAM)", [
                "Mật khẩu tài khoản công việc của bạn có đáp ứng độ phức tạp tối thiểu?",
                "Tần suất bạn thay đổi mật khẩu tài khoản hệ thống quan trọng?",
                "Hệ thống cơ quan có bắt buộc sử dụng Xác thực đa yếu tố (MFA/2FA)?",
                "Bạn có thói quen dùng chung một mật khẩu cho nhiều tài khoản khác nhau?",
                "Mức độ tin cậy của phương thức đăng nhập một lần (Single Sign-On - SSO)?",
                "Bạn có sử dụng ứng dụng tạo mã OTP (Google Authenticator, Microsoft Auth)?",
                "Chính sách khóa tài khoản tự động sau số lần đăng nhập sai liên tiếp?",
                "Quy trình thu hồi quyền truy cập ngay khi nhân sự nghỉ việc có nghiêm ngặt?",
                "Cơ chế phân quyền truy cập theo vai trò công việc (RBAC) có chặt chẽ?",
                "Đề xuất cải tiến phương thức xác thực để vừa an toàn vừa thuận tiện:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("An toàn thiết bị đầu cuối (Endpoint)", [
                "Máy tính làm việc có được cài đặt phần mềm Antivirus/EDR bản quyền?",
                "Hệ điều hành và các phần mềm có được tự động cập nhật bản vá bảo mật?",
                "Chính sách mã hóa ổ đĩa cứng (BitLocker / FileVault) có được kích hoạt?",
                "Bạn có quyền cài đặt phần mềm tùy ý trên máy tính công ty không?",
                "Quy định quản lý thiết bị cá nhân khi kết nối mạng cơ quan (BYOD)?",
                "Các cổng USB có bị khóa tính năng sao chép dữ liệu ra ngoài không?",
                "Máy tính có tự động quét mã độc theo lịch định kỳ hàng tuần không?",
                "Thiết bị di động làm việc có được quản lý tập trung qua MDM không?",
                "Mức độ rủi ro khi để tài liệu mật trên màn hình nền máy tính?",
                "Ý kiến của bạn về các chính sách kiểm soát máy trạm hiện nay:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Phòng chống lừa đảo (Phishing)", [
                "Đơn vị bạn có tổ chức các đợt diễn tập giả lập gửi email lừa đảo không?",
                "Khả năng nhận diện email lừa đảo chứa tệp đính kèm mã độc của bạn?",
                "Quy trình báo cáo email khả nghi cho đội chuyên trách an ninh mạng?",
                "Hệ thống email cơ quan có tính năng lọc thư rác và cảnh báo thư ngoài?",
                "Bạn đã từng nhận được cuộc gọi lừa đảo giả danh công an, ngân hàng chưa?",
                "Mức độ cảnh giác trước các chiêu trò tấn công lừa đảo qua mạng xã hội?",
                "Chính sách xác minh chéo qua điện thoại trước khi chuyển tiền giao dịch?",
                "Thời gian phản hồi của đội IT khi bạn báo cáo một sự cố nghi ngờ bị hack?",
                "Mức độ thiệt hại ước tính nếu một tài khoản email cơ quan bị chiếm quyền?",
                "Đề xuất biện pháp nâng cao kỹ năng phòng chống tấn công lừa đảo:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("An ninh mạng nội bộ & Truy cập từ xa", [
                "Mạng Wi-Fi nội bộ có được phân tách riêng biệt với Wi-Fi cho khách?",
                "Kết nối từ xa vào mạng cơ quan có bắt buộc qua kênh VPN bảo mật?",
                "Hệ thống tường lửa thế hệ mới (NGFW) có kiểm soát lưu lượng hiệu quả?",
                "Khả năng ngăn chặn truy cập vào các trang web đen, độc hại hoặc cờ bạc?",
                "Mức độ ổn định và tốc độ truyền tải khi kết nối qua mạng riêng ảo VPN?",
                "Chính sách phân vùng mạng (Network Segmentation) giữa các phòng ban?",
                "Hệ thống phát hiện và ngăn ngừa xâm nhập (IDS/IPS) có hoạt động tốt?",
                "Khả năng rà soát các thiết bị lạ tự ý cắm vào hệ thống mạng nội bộ?",
                "Mức độ kiểm soát an toàn đường truyền khi nhân viên làm việc tại quán cà phê?",
                "Góp ý về hạ tầng mạng và công cụ kết nối làm việc từ xa:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Bảo mật Đám mây (Cloud Security)", [
                "Doanh nghiệp bạn hiện đang sử dụng nền tảng đám mây nào nhiều nhất?",
                "Mức độ phân định trách nhiệm bảo mật giữa nhà cung cấp Cloud và cơ quan?",
                "Dữ liệu lưu trữ trên đám mây có được mã hóa theo tiêu chuẩn AES-256?",
                "Cấu hình phân quyền truy cập kho lưu trữ (S3/Blob Storage) có an toàn?",
                "Công cụ kiểm tra tuân thủ cấu hình bảo mật đám mây (CSPM) tự động?",
                "Mức độ an tâm của bạn khi chuyển đổi CSDL cốt lõi lên nền tảng Cloud?",
                "Quy trình quản lý các khóa bí mật (API Keys, Secrets) có tập trung?",
                "Khả năng mở rộng quy mô phòng thủ tự động khi bị tấn công từ chối dịch vụ?",
                "Chính sách sao lưu đa vùng (Multi-region Backup) phòng ngừa sự cố thảm họa?",
                "Thách thức lớn nhất khi triển khai bảo mật trên môi trường điện toán đám mây:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Phát triển phần mềm an toàn (DevSecOps)", [
                "Quy trình phát triển phần mềm có tích hợp quét mã nguồn tĩnh (SAST)?",
                "Kiểm thử bảo mật động (DAST) có được thực hiện trước khi phát hành?",
                "Mức độ kiểm soát rủi ro từ các thư viện mã nguồn mở bên thứ ba (SCA)?",
                "Các lỗ hổng bảo mật nghiêm trọng có được ưu tiên khắc phục khẩn cấp?",
                "Đội ngũ lập trình viên có được đào tạo về Top 10 lỗ hổng bảo mật OWASP?",
                "Chính sách không lưu trữ mật khẩu hoặc connection string trong source code?",
                "Quy trình kiểm thử thâm nhập (Penetration Testing) định kỳ hàng năm?",
                "Mức độ an toàn của hệ thống API kết nối với các đối tác bên ngoài?",
                "Môi trường phát triển và kiểm thử có sử dụng dữ liệu khách hàng thật không?",
                "Đề xuất để đẩy mạnh quy trình DevSecOps hiệu quả và thực tế hơn:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Tuân thủ pháp lý & Dữ liệu cá nhân", [
                "Mức độ tuân thủ các quy định về Bảo vệ dữ liệu cá nhân (Nghị định 13/2023)?",
                "Doanh nghiệp đã xây dựng Hồ sơ đánh giá tác động xử lý dữ liệu cá nhân?",
                "Khách hàng có được thông báo rõ ràng và đồng ý trước khi thu thập thông tin?",
                "Quy trình xử lý yêu cầu rút lại sự đồng ý hoặc xóa dữ liệu của khách hàng?",
                "Chính sách bảo mật thông tin có đạt các chứng chỉ quốc tế (ISO 27001)?",
                "Mức độ sẵn sàng báo cáo cơ quan quản lý khi xảy ra sự cố rò rỉ dữ liệu?",
                "Hợp đồng với đối tác xử lý dữ liệu có ràng buộc trách nhiệm bồi thường?",
                "Dữ liệu khách hàng nhạy cảm có được ẩn danh hóa (Anonymization) khi lưu?",
                "Nhận thức của cán bộ nhân viên về chế tài xử phạt khi làm lộ dữ liệu cá nhân?",
                "Khó khăn lớn nhất trong việc tuân thủ các quy định pháp luật về an ninh mạng:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Giám sát (SOC) & Ứng cứu sự cố", [
                "Doanh nghiệp có Trung tâm điều hành an ninh mạng (SOC) giám sát 24/7?",
                "Hệ thống SIEM có thu thập và phân tích nhật ký (Log) tập trung không?",
                "Thời gian trung bình để phát hiện một hành vi xâm nhập trái phép (MTTD)?",
                "Kế hoạch ứng phó sự cố an ninh mạng (IRP) có được ban hành bằng văn bản?",
                "Doanh nghiệp có tổ chức diễn tập ứng phó sự cố tấn công mạng hàng năm?",
                "Khả năng cô lập nhanh chóng máy chủ bị nhiễm mã độc tống tiền (Ransomware)?",
                "Hợp đồng hỗ trợ ứng cứu sự cố với các đơn vị bảo mật chuyên nghiệp bên ngoài?",
                "Quy trình điều tra nguyên nhân gốc rễ và phân tích chứng cứ số (Forensics)?",
                "Bài học kinh nghiệm sau mỗi sự cố có được phổ biến cho toàn đơn vị?",
                "Đề xuất để nâng cao năng lực ứng cứu sự cố của cơ quan:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Đầu tư, Ứng dụng AI & Tương lai", [
                "Tỷ lệ ngân sách dành cho an toàn thông tin so với tổng ngân sách công nghệ?",
                "Doanh nghiệp có kế hoạch triển khai kiến trúc Không tin tưởng (Zero Trust)?",
                "Mức độ ứng dụng Trí tuệ nhân tạo (AI/ML) trong việc phát hiện mối đe dọa?",
                "Khả năng đối phó với các cuộc tấn công tinh vi sử dụng công nghệ Deepfake?",
                "Doanh nghiệp có mua bảo hiểm rủi ro an ninh mạng (Cyber Insurance) không?",
                "Mức độ sẵn sàng trước các cuộc tấn công chuỗi cung ứng (Supply Chain Attack)?",
                "Chiến lược tuyển dụng và giữ chân chuyên gia an toàn thông tin chất lượng cao?",
                "Sự quan tâm và cam kết của Ban lãnh đạo cấp cao đối với an ninh mạng?",
                "Đánh giá mức độ an toàn thông tin tổng thể của đơn vị bạn hiện nay?",
                "Kiến nghị giải pháp trọng tâm để nâng tầm lá chắn an ninh mạng đơn vị:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"])
        };

        var multiOpts = new[]
        {
            new[] { "Tấn công lừa đảo qua email (Phishing)", "Mã độc mã hóa tống tiền (Ransomware)", "Rò rỉ dữ liệu qua nhân sự nội bộ", "Lỗ hổng zero-day chưa kịp vá", "Tấn công từ chối dịch vụ phân tán (DDoS)" },
            new[] { "Amazon Web Services (AWS)", "Microsoft Azure", "Google Cloud Platform (GCP)", "Hạ tầng Cloud trong nước (Viettel, VNPT)", "Hạ tầng máy chủ vật lý riêng (On-premise)" },
            new[] { "Xác thực đa yếu tố bắt buộc (MFA)", "Phần mềm diệt virus EDR tập trung", "Hệ thống tường lửa thế hệ mới", "Mã hóa dữ liệu toàn diện", "Đào tạo nhận thức an ninh mạng định kỳ" },
            new[] { "Thiếu hụt ngân sách đầu tư", "Thiếu nhân sự chuyên trách có năng lực", "Ý thức nhân viên còn chủ quan", "Hệ thống cũ khó nâng cấp bảo mật", "Quy trình thủ tục còn rườm rà" }
        };

        return Create100QuestionsSurvey(
            adminId,
            "khao-sat-an-toan-thong-tin-chuyen-doi-so-100",
            "Khảo sát toàn diện an toàn thông tin, bảo mật dữ liệu và chuyển đổi số (100 câu hỏi)",
            "Đánh giá chuyên sâu 10 trụ cột bảo mật không gian số: nhận thức cá nhân, danh tính IAM, an toàn máy trạm, phòng chống phishing, mạng nội bộ, an ninh đám mây, DevSecOps, tuân thủ Nghị định 13, SOC ứng cứu sự cố và chiến lược Zero Trust.",
            25,
            15,
            topics,
            multiOpts);
    }

    // =========================================================================
    // 5. KHẢO SÁT 5: DỊCH VỤ Y TẾ & CHĂM SÓC SỨC KHỎE (100 CÂU HỎI)
    // =========================================================================
    private static Survey BuildSurveyHealthcare100(string adminId)
    {
        var topics = new (string Category, string[] Prompts, string[] Types)[]
        {
            ("Thói quen & Nhu cầu khám sức khỏe", [
                "Tần suất bạn và người thân đi kiểm tra sức khỏe tổng quát định kỳ?",
                "Bạn thường lựa chọn khám chữa bệnh tại cơ sở y tế công lập hay tư nhân?",
                "Lý do quan trọng nhất khiến bạn quyết định lựa chọn một bệnh viện?",
                "Bạn có thói quen tự mua thuốc điều trị khi có triệu chứng bệnh nhẹ?",
                "Mức độ quan tâm của bạn đến chế độ dinh dưỡng và lối sống lành mạnh?",
                "Bạn có thường xuyên tìm hiểu kiến thức y khoa trên các kênh truyền thông?",
                "Tần suất bạn tham gia các hoạt động thể dục thể thao rèn luyện thể chất?",
                "Bạn đã từng tham gia khám sức khỏe tiền hôn nhân hoặc tầm soát ung thư chưa?",
                "Mức độ chủ động tiêm phòng vắc xin nhắc lại hàng năm (như cúm mùa)?",
                "Kỳ vọng lớn nhất của bạn đối với hệ thống y tế chăm sóc sức khỏe ban đầu:"
            ], ["single_choice", "single_choice", "multiple_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Đăng ký khám & Ứng dụng số", [
                "Hình thức đặt lịch khám bệnh bạn thường áp dụng nhiều nhất hiện nay?",
                "Ứng dụng di động của bệnh viện có giúp tiết kiệm thời gian chờ đợi?",
                "Giao diện ứng dụng đặt lịch hẹn khám bệnh có trực quan, dễ thao tác?",
                "Khả năng chủ động lựa chọn bác sĩ khám và khung giờ mong muốn trên app?",
                "Hệ thống phát số thứ tự tự động bằng máy kiosk tại bệnh viện hoạt động tốt?",
                "Thông báo nhắc lịch khám qua tin nhắn Zalo/SMS có kịp thời và chính xác?",
                "Khả năng tra cứu kết quả xét nghiệm và đơn thuốc điện tử trên ứng dụng?",
                "Mức độ thuận tiện khi liên thông dữ liệu y tế từ ứng dụng VNeID / VssID?",
                "Bạn đánh giá thế nào về tính năng chỉ đường thông minh trong khuôn viên viện?",
                "Góp ý của bạn để hoàn thiện ứng dụng số phục vụ bệnh nhân:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Tiếp đón & Thời gian chờ đợi", [
                "Thái độ tiếp đón và hướng dẫn của nhân viên tại bàn tiếp đón ban đầu?",
                "Thời gian chờ đợi từ lúc lấy số thứ tự đến khi được vào phòng khám?",
                "Khu vực ngồi chờ khám bệnh có đủ ghế ngồi, quạt mát và nước uống?",
                "Hệ thống loa và màn hình hiển thị gọi tên bệnh nhân có rõ ràng?",
                "Thủ tục tiếp nhận bệnh nhân cấp cứu có diễn ra nhanh chóng, khẩn trương?",
                "Sự hỗ trợ tận tình dành cho người cao tuổi, người khuyết tật và trẻ nhỏ?",
                "Biển chỉ dẫn các phòng khám và khoa chuyên môn có dễ tìm kiếm?",
                "Quy trình phân luồng người bệnh theo mức độ ưu tiên có hợp lý?",
                "Thời gian thực hiện các thủ tục hành chính ban đầu có được rút ngắn?",
                "Đề xuất giải pháp nhằm giảm tải thời gian chờ đợi tại khoa khám bệnh:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Năng lực Bác sĩ & Tư vấn chuyên môn", [
                "Bác sĩ có lắng nghe kỹ lưỡng các triệu chứng và tiền sử bệnh của bạn?",
                "Thái độ giao tiếp, giải thích bệnh tình của bác sĩ có nhã nhặn, thấu hiểu?",
                "Bác sĩ có giải thích rõ ràng về tác dụng và cách dùng của từng loại thuốc?",
                "Thời gian bác sĩ dành để thăm khám trực tiếp cho bạn có thỏa đáng?",
                "Mức độ tin tưởng của bạn đối với trình độ chuyên môn của đội ngũ y bác sĩ?",
                "Bác sĩ có tư vấn thêm về chế độ ăn uống, kiêng khem và sinh hoạt phù hợp?",
                "Quyết định chỉ định xét nghiệm của bác sĩ có thực sự cần thiết và hợp lý?",
                "Bạn có được quyền trao đổi và lựa chọn phương án điều trị tối ưu?",
                "Cảm nhận của bạn về y đức và sự tận tâm của bác sĩ điều trị?",
                "Góp ý cụ thể dành cho đội ngũ bác sĩ trực tiếp thăm khám:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Điều dưỡng, Kỹ thuật viên & Hộ lý", [
                "Thái độ phục vụ và kỹ năng giao tiếp của đội ngũ điều dưỡng viên?",
                "Kỹ thuật tiêm, truyền dịch và lấy mẫu máu xét nghiệm có nhẹ nhàng, chuẩn xác?",
                "Thời gian điều dưỡng có mặt hỗ trợ khi bạn bấm chuông gọi tại giường?",
                "Sự tận tình hướng dẫn người bệnh chuẩn bị trước khi làm thủ thuật?",
                "Trang phục và tác phong làm việc của nhân viên y tế có chuẩn mực?",
                "Sự quan tâm, động viên tinh thần người bệnh trong quá trình điều trị?",
                "Đội ngũ hộ lý có hỗ trợ chu đáo việc di chuyển bệnh nhân bằng xe lăn/cáng?",
                "Việc đo sinh hiệu (huyết áp, nhiệt độ) có được thực hiện đều đặn?",
                "Mức độ chuyên nghiệp của kỹ thuật viên tại các phòng chụp chiếu?",
                "Nhận xét của bạn về sự chăm sóc của đội ngũ điều dưỡng, hộ lý:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Dịch vụ Cận lâm sàng & Xét nghiệm", [
                "Thời gian chờ đợi để thực hiện lấy mẫu xét nghiệm máu và nước tiểu?",
                "Thời gian trả kết quả xét nghiệm có đúng hẹn như thông báo?",
                "Chất lượng phòng chụp X-quang, CT-Scanner và cộng hưởng từ (MRI)?",
                "Kỹ thuật viên có hướng dẫn cặn kẽ tư thế và yêu cầu khi chụp chiếu?",
                "Phòng siêu âm, nội soi có đảm bảo sự riêng tư và vệ sinh cho bệnh nhân?",
                "Độ chính xác và độ tin cậy của kết quả xét nghiệm tại bệnh viện?",
                "Kết quả xét nghiệm có được tự động đồng bộ lên bệnh án điện tử?",
                "Không gian phòng chờ xét nghiệm có thoáng mát và trật tự không?",
                "Thủ tục nhận kết quả xét nghiệm có nhanh gọn, thuận tiện?",
                "Đề xuất để đẩy nhanh tiến độ trả kết quả cận lâm sàng:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Cấp phát thuốc & Nhà thuốc bệnh viện", [
                "Quy trình lấy số và nhận thuốc theo bảo hiểm y tế có trật tự?",
                "Dược sĩ có hướng dẫn chi tiết liều dùng và thời điểm uống từng loại thuốc?",
                "Danh mục thuốc BHYT có đáp ứng đầy đủ các loại thuốc điều trị cần thiết?",
                "Tình trạng thiếu thuốc hoặc yêu cầu bệnh nhân mua ngoài có hay xảy ra?",
                "Mức giá thuốc tại nhà thuốc bệnh viện có hợp lý so với thị trường ngoài?",
                "Chất lượng bao bì, tem nhãn và hạn sử dụng của thuốc được cấp phát?",
                "Thời gian chờ đợi từ lúc nộp đơn thuốc đến khi nhận được thuốc?",
                "Dược sĩ có cảnh báo về các tác dụng phụ không mong muốn của thuốc?",
                "Sự tiện lợi của dịch vụ giao thuốc tận nhà cho bệnh nhân mãn tính?",
                "Ý kiến đóng góp về công tác cấp phát thuốc và nhà thuốc bệnh viện:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "text"]),

            ("Bảo hiểm y tế & Viện phí", [
                "Mức độ thuận tiện khi sử dụng thẻ Căn cước công dân gắn chip khám BHYT?",
                "Quy trình giám định và thanh toán bảo hiểm y tế có minh bạch, nhanh gọn?",
                "Chi phí đồng chi trả (20% hoặc 5%) có được giải thích rõ ràng?",
                "Bệnh viện có hỗ trợ đa dạng phương thức thanh toán không dùng tiền mặt?",
                "Bảng giá các dịch vụ khám bệnh và xét nghiệm có được công khai rõ ràng?",
                "Hóa đơn điện tử và bảng kê chi tiết viện phí có minh bạch, dễ hiểu?",
                "Chính sách hỗ trợ chi phí cho bệnh nhân có hoàn cảnh khó khăn?",
                "Mức chi trả của BHYT cho các dịch vụ kỹ thuật cao có thỏa đáng?",
                "Thủ tục chuyển tuyến (giấy chuyển viện) có gặp nhiều phiền hà không?",
                "Đề xuất để đơn giản hóa thủ tục thanh toán viện phí và BHYT:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Vệ sinh phòng ốc, Môi trường & Tiện ích", [
                "Mức độ sạch sẽ và khử khuẩn của các buồng bệnh điều trị nội trú?",
                "Hệ thống nhà vệ sinh bệnh viện có đảm bảo sạch sẽ, khô ráo, không mùi?",
                "Chất lượng chăn ga, gối đệm và trang phục bệnh nhân có thơm tho, sạch sẽ?",
                "Chất lượng các bữa ăn dinh dưỡng do bệnh viện cung cấp cho người bệnh?",
                "Mức độ yên tĩnh và an ninh trật tự trong bệnh viện vào ban đêm?",
                "Khuôn viên bệnh viện có nhiều cây xanh, ghế đá và không gian thư giãn?",
                "Bãi đỗ xe bệnh viện có thuận tiện, an toàn và mức giá gửi xe hợp lý?",
                "Căng tin và cửa hàng tiện lợi trong bệnh viện có phục vụ chu đáo?",
                "Quy trình thu gom và phân loại rác thải y tế có đảm bảo an toàn?",
                "Góp ý về việc cải thiện cơ sở vật chất và môi trường bệnh viện:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"]),

            ("Đánh giá tổng thể & Phát triển bền vững", [
                "Mức độ hài lòng chung đối với toàn bộ trải nghiệm khám chữa bệnh đợt này?",
                "Hiệu quả điều trị và mức độ thuyên giảm bệnh sau khi tuân thủ phác đồ?",
                "Bạn có sẵn lòng giới thiệu bệnh viện này cho người thân và bạn bè?",
                "Khả năng bạn tiếp tục lựa chọn bệnh viện này khi có nhu cầu khám sau này?",
                "Mức độ hài lòng về kênh tiếp nhận phản ánh đường dây nóng (Hotline)?",
                "Chính sách chăm sóc khách hàng và hỏi thăm sức khỏe sau xuất viện?",
                "Mức độ ứng dụng công nghệ hiện đại trong chẩn đoán và điều trị bệnh?",
                "Bạn đánh giá thế nào về sự công bằng trong phục vụ y tế tại bệnh viện?",
                "Yếu tố bạn ấn tượng và hài lòng nhất tại cơ sở y tế này?",
                "Đóng góp ý kiến tâm huyết nhất để nâng tầm chất lượng dịch vụ y tế nhân dân:"
            ], ["single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "single_choice", "textarea"])
        };

        var multiOpts = new[]
        {
            new[] { "Trình độ chuyên môn của Bác sĩ", "Trang thiết bị y tế hiện đại", "Thái độ phục vụ tận tâm", "Chi phí điều trị hợp lý", "Thủ tục nhanh gọn và gần nhà" },
            new[] { "Chờ đợi khám bệnh quá lâu", "Thủ tục hành chính BHYT phức tạp", "Thái độ nhân viên y tế chưa nhã nhặn", "Cơ sở vật chất phòng bệnh xuống cấp", "Thiếu thông tin hướng dẫn rõ ràng" },
            new[] { "Thanh toán viện phí quét mã QR / VNPAY", "Chuyển khoản qua số tài khoản bệnh viện", "Thanh toán bằng thẻ ATM / Thẻ tín dụng", "Ví điện tử (MoMo, ZaloPay)", "Nộp tiền mặt truyền thống" },
            new[] { "Bác sĩ chuyên môn cao và tận tâm", "Máy móc chẩn đoán hiện đại", "Không gian bệnh viện xanh - sạch - đẹp", "Ứng dụng đặt lịch khám tiện lợi", "Chi phí rõ ràng, minh bạch" }
        };

        return Create100QuestionsSurvey(
            adminId,
            "khao-sat-dich-vu-y-te-suc-khoe-100",
            "Khảo sát toàn diện chất lượng dịch vụ y tế, bảo hiểm và chăm sóc sức khỏe (100 câu hỏi)",
            "Bộ chỉ số 100 câu hỏi đánh giá chuẩn mực chất lượng bệnh viện: thói quen khám, đăng ký online, tiếp đón, chuyên môn bác sĩ, chăm sóc điều dưỡng, xét nghiệm chẩn đoán, dược phẩm, BHYT viện phí, vệ sinh buồng bệnh và sự hài lòng người bệnh.",
            25,
            18,
            topics,
            multiOpts);
    }

    // =========================================================================
    // HÀM SINH PHẢN HỒI MẪU (RESPONSES GENERATOR)
    // =========================================================================
    private static List<SurveyResponse> GenerateSampleResponses(Survey survey, int count)
    {
        var random = new Random(survey.Slug.GetHashCode());
        var responses = new List<SurveyResponse>();

        for (int i = 1; i <= count; i++)
        {
            var nameIndex = (i - 1) % SampleNames.Length;
            var name = SampleNames[nameIndex];
            var email = $"user{i}.{survey.Slug.Replace("-", "")[..Math.Min(8, survey.Slug.Length)]}@example.com";
            var phone = $"09{random.Next(10000000, 99999999)}";
            var submittedTime = DateTime.UtcNow.AddDays(-random.Next(1, 14)).AddMinutes(-random.Next(10, 1400));

            var response = new SurveyResponse
            {
                Id = ObjectId.GenerateNewId().ToString(),
                SurveyId = survey.Id,
                SurveyVersion = survey.Version,
                RespondentName = name,
                RespondentEmail = email,
                RespondentPhone = phone,
                SubmittedAt = submittedTime,
                Answers = []
            };

            foreach (var question in survey.Questions)
            {
                // Nếu câu hỏi không bắt buộc, có 15% khả năng bỏ qua
                if (!question.Required && random.Next(100) < 15)
                {
                    continue;
                }

                var answer = new SurveyAnswer { QuestionId = question.Id, Values = [] };

                if (question.Type == "single_choice" && question.Options.Count > 0)
                {
                    // Phân bổ chọn phương án tự nhiên (thiên về các phương án tốt / hài lòng)
                    int selectedIndex;
                    int roll = random.Next(100);
                    if (roll < 35 && question.Options.Count >= 5) selectedIndex = 4; // Rất hài lòng
                    else if (roll < 70 && question.Options.Count >= 4) selectedIndex = 3; // Hài lòng
                    else if (roll < 90 && question.Options.Count >= 3) selectedIndex = 2; // Bình thường
                    else selectedIndex = random.Next(question.Options.Count);

                    selectedIndex = Math.Clamp(selectedIndex, 0, question.Options.Count - 1);
                    answer.Values.Add(question.Options[selectedIndex].Id);
                }
                else if (question.Type == "multiple_choice" && question.Options.Count > 0)
                {
                    // Chọn từ 1 đến 3 đáp án ngẫu nhiên
                    int pickCount = random.Next(1, Math.Min(4, question.Options.Count + 1));
                    var shuffled = question.Options.OrderBy(_ => random.Next()).Take(pickCount);
                    answer.Values.AddRange(shuffled.Select(o => o.Id));
                }
                else if (question.Type == "text")
                {
                    answer.Values.Add($"Đóng góp ý kiến cho câu hỏi {question.Order} từ người trả lời {name}.");
                }
                else if (question.Type == "textarea")
                {
                    var snippet = SampleFeedbackSnippets[random.Next(SampleFeedbackSnippets.Length)];
                    answer.Values.Add(snippet);
                }

                response.Answers.Add(answer);
            }

            responses.Add(response);
        }

        return responses;
    }
}
