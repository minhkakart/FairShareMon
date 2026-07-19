namespace FairShareMonApi.Services.Api.Banks;

/// <summary>
/// Committed static VietQR bank-directory snapshot used as the offline/CORS fallback so the banks endpoint
/// never fails. Ported verbatim from <c>FairShareMonWeb/src/features/wallet/data/vietqrBanks.ts</c>.
///
/// SOURCE: GET https://vietqr.vn/api/vietqr/banks
/// CAPTURED: 2026-07-18 (58 banks kept of 66 returned; 8 dropped for a non-6-digit caiValue). Already
/// normalized to { bin, code, name, shortName, imageId } and sorted by short name. To refresh, re-fetch the
/// endpoint, normalize (drop non-^\d{6}$ BINs), and re-bake this array.
/// </summary>
internal static class BankDirectoryFallback
{
    /// <summary>The committed 58-bank snapshot.</summary>
    public static readonly IReadOnlyList<ProviderBank> Snapshot =
    [
        new("970425", "ABB", "Ngân hàng TMCP An Bình", "ABBANK", "6435c9f3-6394-4933-874c-e82634ea78bb"),
        new("970416", "ACB", "Ngân hàng TMCP Á Châu", "ACB", "e917e05e-c370-4ae7-82c9-e16c9200b3fe"),
        new("970405", "AGR", "Ngân hàng Nông nghiệp và Phát triển Nông thôn Việt Nam", "Agribank", "6cbf8835-7615-4f87-bbdb-56ee7ad7839a"),
        new("970409", "BAB", "Ngân hàng TMCP Bắc Á", "BacABank", "721c5812-db4d-4b16-8265-5875458cd3c9"),
        new("970438", "BVB", "Ngân hàng TMCP Bảo Việt", "BaoVietBank", "7b4cee38-72f5-4fb4-bef3-475b776a3801"),
        new("970418", "BIDV", "Ngân hàng TMCP Đầu tư và Phát triển Việt Nam", "BIDV", "cb18c1b3-d661-4695-b2e8-dba8e887abd6"),
        new("546034", "CAKE", "TMCP Việt Nam Thịnh Vượng - Ngân hàng số CAKE by VPBank", "CAKE", "69296759-ddcf-419c-9bbe-c2533f7ec025"),
        new("970444", "CBB", "Ngân hàng Thương mại TNHH MTV Xây dựng Việt Nam", "CBBank", "adcc8221-b38f-46ad-a7da-7c77604d4cc7"),
        new("422589", "CIMB", "Ngân hàng TNHH MTV CIMB Việt Nam", "CIMB", "55bca05e-5d46-498e-a612-928f1ff418b0"),
        new("970446", "COOPBANK", "Ngân hàng Hợp tác xã Việt Nam", "COOPBANK", "7d8d778e-d00b-4925-aff6-eb5dccc4052d"),
        new("796500", "DBS", "DBS Bank Ltd - Chi nhánh Thành phố Hồ Chí Minh", "DBSBank", "cf7e309a-739f-47ed-b0f8-fddf01ca21b0"),
        new("970406", "DAB", "Ngân hàng TMCP Đông Á", "DongABank", "acc08fda-dc3f-4617-a9bb-c8f5cd3e1bc8"),
        new("970431", "EIB", "Ngân hàng TMCP Xuất Nhập khẩu Việt Nam", "Eximbank", "b3426240-7c27-4d3e-8b70-63c20145b480"),
        new("970408", "GPB", "Ngân hàng Thương mại TNHH MTV Dầu Khí Toàn Cầu", "GPBank", "b4df2cee-f291-4be8-8398-bd397ce7ab95"),
        new("970437", "HDB", "Ngân hàng TMCP Phát triển Thành phố Hồ Chí Minh", "HDBank", "39c89122-464a-40ab-82d6-cc0c1a45ca21"),
        new("970442", "HONGLEONG", "Ngân hàng TNHH MTV Hongleong Việt Nam", "HongLeong", "75f5860f-776a-46fe-92f8-7bf8d166d86b"),
        new("458761", "HSBC", "Ngân hàng TNHH MTV HSBC (Việt Nam)", "HSBC", "6da4487e-c131-4162-b3c6-2b8dac8b38e3"),
        new("970456", "IBK", "Ngân hàng Công nghiệp Hàn Quốc", "IBK", "30a3be74-1fa5-49e7-90be-c9f8a48a1cda"),
        new("970434", "IVB", "Ngân hàng TNHH Indovina", "IndovinaBank", "b50fcf91-5498-411c-967c-9c2e7cb5227f"),
        new("668888", "KPB", "Ngân hàng Đại chúng Kasikornbank - Chi nhánh TP. Hồ Chí Minh", "KBank", "c6b64a4f-afef-4d61-aeff-fd2bda0224f2"),
        new("970452", "UMEE", "Ngân hàng số Umee – Kiên Long Bank", "KienLongBank", "4d3d89e6-3edd-41a5-b1e1-06c7b43e1050"),
        new("970452", "KLB", "Ngân hàng TMCP Kiên Long", "KienLongBank", "e827eab3-9d96-47fc-ac8b-926f2e8be383"),
        new("970463", "KBHCM", "Ngân hàng Kookmin - Chi nhánh Thành phố Hồ Chí Minh", "KookminHCM", "46b39ff6-8746-4236-9a9c-f0a9a8da6379"),
        new("970462", "KBHN", "Ngân hàng Kookmin - Chi nhánh Hà Nội", "KookminHN", "50b207cc-5c19-4b17-b609-c7b14e6ed0da"),
        new("970449", "LPB", "Ngân hàng TMCP Lộc Phát Việt Nam", "LPBank", "27ffa8a1-534b-4262-8a8d-cd8530923377"),
        new("970422", "MB", "Ngân hàng TMCP Quân đội", "MBBank", "58b7190b-a294-4b14-968f-cd365593893e"),
        new("970426", "MSB", "Ngân hàng TMCP Hàng Hải", "MSB", "383ff0f8-d293-4916-98ff-cb88040a74ff"),
        new("970428", "NAB", "Ngân hàng TMCP Nam Á", "NamABank", "3833ba06-b272-4922-8c6d-1ce738bd93fd"),
        new("970419", "NCB", "Ngân hàng TMCP Quốc Dân", "NCB", "b87583b2-bcb5-4625-8f5c-17dac9e5ea61"),
        new("801011", "NHB", "Ngân hàng Nonghyup - Chi nhánh Hà Nội", "Nonghyup", "aadd969d-01bd-4041-8db1-f2e8d8bbd271"),
        new("970448", "OCB", "Ngân hàng TMCP Phương Đông", "OCB", "49d927d2-905d-42e2-a64e-4326da4b729f"),
        new("970414", "MBV", "Ngân hàng Trách nhiệm hữu hạn Một thành viên Việt Nam Hiện Đại", "Oceanbank", "e8e63c02-26a0-493a-ab70-e9e022241b0f"),
        new("970430", "PGB", "Ngân hàng TMCP Xăng dầu Petrolimex", "PGBank", "56f4e977-add6-405f-96a9-4f39d353002e"),
        new("970439", "PBVN", "Ngân hàng TNHH MTV Public Việt Nam", "PublicBank", "f63aac51-4385-4691-ba13-0e515ed1519f"),
        new("970412", "PVCB", "Ngân hàng TMCP Đại Chúng Việt Nam", "PVcomBank", "62fbd4d2-abda-4eb9-b465-9cf33d06037c"),
        new("970403", "STB", "Ngân hàng TMCP Sài Gòn Thương Tín", "Sacombank", "a27bd680-86b4-438c-8ff0-633120c3438a"),
        new("970400", "SBC", "Ngân hàng TMCP Sài Gòn Công Thương", "SaigonBank", "9b05cf95-6544-422d-9976-01d59e52d3b5"),
        new("970429", "SCB", "Ngân hàng TMCP Sài Gòn", "SCB", "699e99a6-cf77-4689-889b-d6c10db3bb37"),
        new("970440", "SAB", "Ngân hàng TMCP Đông Nam Á", "SeABank", "d1838a8e-3f84-4fc2-8168-a18fcb72662b"),
        new("970443", "SHB", "Ngân hàng TMCP Sài Gòn - Hà Nội", "SHB", "b872b705-3d9f-4702-a723-00b008b7ffa7"),
        new("970424", "SHINHAN", "Ngân hàng TNHH MTV Shinhan Việt Nam", "ShinhanBank", "9a05a55c-b946-4513-9e44-b3493b29b25b"),
        new("970410", "SCVN", "Ngân hàng TNHH MTV Standard Chartered Bank Việt Nam", "StandardChartered", "55e37047-6426-4883-8f97-d5c3ede07c36"),
        new("970407", "TCB", "Ngân hàng TMCP Kỹ thương Việt Nam", "Techcombank", "97c7b39e-812c-48b5-8126-16e187cfe91b"),
        new("963388", "TIMO", "Ngân hàng số Timo by Ban Viet Bank (Timo by Ban Viet Bank)", "Timo", "af005597-37a7-463f-831c-564b851de853"),
        new("970423", "TPB", "Ngân hàng TMCP Tiên Phong", "TPBank", "40461b0d-d370-4b3f-974a-b7798a18952e"),
        new("546035", "Ubank", "TMCP Việt Nam Thịnh Vượng - Ngân hàng số Ubank by VPBank", "Ubank", "fc89bc9b-a11e-4bf9-8d90-e3b4db5e41a0"),
        new("970458", "UOB", "Ngân hàng United Overseas - Chi nhánh TP. Hồ Chí Minh", "UnitedOverseas", "247d67c4-b434-4de8-9a63-278de158255c"),
        new("970441", "VIB", "Ngân hàng TMCP Quốc tế Việt Nam", "VIB", "d7fff155-734b-46f1-ba31-d0aef434e1ba"),
        new("970427", "VAB", "Ngân hàng TMCP Việt Á", "VietABank", "5bf5bf2a-53b1-4c0e-959f-f4aa87966d65"),
        new("970433", "VBC", "Ngân hàng TMCP Việt Nam Thương Tín", "VietBank", "8fdc43c4-0cc7-46eb-82c7-a4813dac60d3"),
        new("970454", "VCAB", "Ngân hàng TMCP Bản Việt", "VietCapitalBank", "e6f12053-6283-4359-97b1-28e0cf4141ce"),
        new("970436", "VCB", "Ngân hàng TMCP Ngoại Thương Việt Nam", "Vietcombank", "d0e196fc-3d4c-4501-b453-ac8c3df968cf"),
        new("970415", "VTB", "Ngân hàng TMCP Công thương Việt Nam", "Vietinbank", "22abf9a2-6ede-4e48-8b8a-9c8fc1303c22"),
        new("971005", "VTLMONEY", "Tổng Công ty Dịch vụ số Viettel - Chi nhánh tập đoàn công nghiệp viễn thông Quân Đội", "ViettelMoney", "2cd19eb5-2a5a-4648-8d32-3bd669a4a2ed"),
        new("971011", "VNPTMONEY", "Trung tâm dịch vụ tài chính số VNPT- Chi nhánh Tổng công ty truyền thông (VNPT Fintech)", "VNPTMoney", "b4e8c5b0-0e67-4ea9-a214-1f5f12caf25f"),
        new("970432", "VPB", "Ngân hàng TMCP Việt Nam Thịnh Vượng", "VPBank", "029446c5-396b-49a1-adf2-482f2a45b8e0"),
        new("970421", "VRB", "Ngân hàng Liên doanh Việt - Nga", "VRB", "17c01559-494a-454b-8cda-c8cc4d61e89e"),
        new("970457", "WRB", "Ngân hàng TNHH MTV Woori Việt Nam", "Woori", "2fd79b2b-bb01-48dc-9964-c8455da13a7e"),
    ];
}
