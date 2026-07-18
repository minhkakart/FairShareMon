/**
 * Committed VietQR bank-directory snapshot.
 *
 * This is BOTH the instant seed for the picker (`useVietqrBanks` `initialData`,
 * so the list is populated on first paint) AND the offline/CORS fallback (prod is
 * a static SPA with no proxy — the live JSON fetch can fail, but the picker must
 * never be empty). Already normalized to `VietqrBank` (invalid non-6-digit BINs
 * dropped) and sorted by short name.
 *
 * SOURCE: GET https://vietqr.vn/api/vietqr/banks
 * CAPTURED: 2026-07-18 (FULL live snapshot — 58 banks kept of 66 returned; 8
 *   dropped for a non-6-digit caiValue). To refresh, re-fetch the endpoint,
 *   normalize (see `vietqrDirectoryApi.list`), and re-bake this array.
 */
import type { VietqrBank } from "../api/vietqrDirectoryApi";

export const VIETQR_BANKS_SNAPSHOT: VietqrBank[] = [
  { bin: "970425", code: "ABB", name: "Ngân hàng TMCP An Bình", shortName: "ABBANK", imageId: "6435c9f3-6394-4933-874c-e82634ea78bb" },
  { bin: "970416", code: "ACB", name: "Ngân hàng TMCP Á Châu", shortName: "ACB", imageId: "e917e05e-c370-4ae7-82c9-e16c9200b3fe" },
  { bin: "970405", code: "AGR", name: "Ngân hàng Nông nghiệp và Phát triển Nông thôn Việt Nam", shortName: "Agribank", imageId: "6cbf8835-7615-4f87-bbdb-56ee7ad7839a" },
  { bin: "970409", code: "BAB", name: "Ngân hàng TMCP Bắc Á", shortName: "BacABank", imageId: "721c5812-db4d-4b16-8265-5875458cd3c9" },
  { bin: "970438", code: "BVB", name: "Ngân hàng TMCP Bảo Việt", shortName: "BaoVietBank", imageId: "7b4cee38-72f5-4fb4-bef3-475b776a3801" },
  { bin: "970418", code: "BIDV", name: "Ngân hàng TMCP Đầu tư và Phát triển Việt Nam", shortName: "BIDV", imageId: "cb18c1b3-d661-4695-b2e8-dba8e887abd6" },
  { bin: "546034", code: "CAKE", name: "TMCP Việt Nam Thịnh Vượng - Ngân hàng số CAKE by VPBank", shortName: "CAKE", imageId: "69296759-ddcf-419c-9bbe-c2533f7ec025" },
  { bin: "970444", code: "CBB", name: "Ngân hàng Thương mại TNHH MTV Xây dựng Việt Nam", shortName: "CBBank", imageId: "adcc8221-b38f-46ad-a7da-7c77604d4cc7" },
  { bin: "422589", code: "CIMB", name: "Ngân hàng TNHH MTV CIMB Việt Nam", shortName: "CIMB", imageId: "55bca05e-5d46-498e-a612-928f1ff418b0" },
  { bin: "970446", code: "COOPBANK", name: "Ngân hàng Hợp tác xã Việt Nam", shortName: "COOPBANK", imageId: "7d8d778e-d00b-4925-aff6-eb5dccc4052d" },
  { bin: "796500", code: "DBS", name: "DBS Bank Ltd - Chi nhánh Thành phố Hồ Chí Minh", shortName: "DBSBank", imageId: "cf7e309a-739f-47ed-b0f8-fddf01ca21b0" },
  { bin: "970406", code: "DAB", name: "Ngân hàng TMCP Đông Á", shortName: "DongABank", imageId: "acc08fda-dc3f-4617-a9bb-c8f5cd3e1bc8" },
  { bin: "970431", code: "EIB", name: "Ngân hàng TMCP Xuất Nhập khẩu Việt Nam", shortName: "Eximbank", imageId: "b3426240-7c27-4d3e-8b70-63c20145b480" },
  { bin: "970408", code: "GPB", name: "Ngân hàng Thương mại TNHH MTV Dầu Khí Toàn Cầu", shortName: "GPBank", imageId: "b4df2cee-f291-4be8-8398-bd397ce7ab95" },
  { bin: "970437", code: "HDB", name: "Ngân hàng TMCP Phát triển Thành phố Hồ Chí Minh", shortName: "HDBank", imageId: "39c89122-464a-40ab-82d6-cc0c1a45ca21" },
  { bin: "970442", code: "HONGLEONG", name: "Ngân hàng TNHH MTV Hongleong Việt Nam", shortName: "HongLeong", imageId: "75f5860f-776a-46fe-92f8-7bf8d166d86b" },
  { bin: "458761", code: "HSBC", name: "Ngân hàng TNHH MTV HSBC (Việt Nam)", shortName: "HSBC", imageId: "6da4487e-c131-4162-b3c6-2b8dac8b38e3" },
  { bin: "970456", code: "IBK", name: "Ngân hàng Công nghiệp Hàn Quốc", shortName: "IBK", imageId: "30a3be74-1fa5-49e7-90be-c9f8a48a1cda" },
  { bin: "970434", code: "IVB", name: "Ngân hàng TNHH Indovina", shortName: "IndovinaBank", imageId: "b50fcf91-5498-411c-967c-9c2e7cb5227f" },
  { bin: "668888", code: "KPB", name: "Ngân hàng Đại chúng Kasikornbank - Chi nhánh TP. Hồ Chí Minh", shortName: "KBank", imageId: "c6b64a4f-afef-4d61-aeff-fd2bda0224f2" },
  { bin: "970452", code: "UMEE", name: "Ngân hàng số Umee – Kiên Long Bank", shortName: "KienLongBank", imageId: "4d3d89e6-3edd-41a5-b1e1-06c7b43e1050" },
  { bin: "970452", code: "KLB", name: "Ngân hàng TMCP Kiên Long", shortName: "KienLongBank", imageId: "e827eab3-9d96-47fc-ac8b-926f2e8be383" },
  { bin: "970463", code: "KBHCM", name: "Ngân hàng Kookmin - Chi nhánh Thành phố Hồ Chí Minh", shortName: "KookminHCM", imageId: "46b39ff6-8746-4236-9a9c-f0a9a8da6379" },
  { bin: "970462", code: "KBHN", name: "Ngân hàng Kookmin - Chi nhánh Hà Nội", shortName: "KookminHN", imageId: "50b207cc-5c19-4b17-b609-c7b14e6ed0da" },
  { bin: "970449", code: "LPB", name: "Ngân hàng TMCP Lộc Phát Việt Nam", shortName: "LPBank", imageId: "27ffa8a1-534b-4262-8a8d-cd8530923377" },
  { bin: "970422", code: "MB", name: "Ngân hàng TMCP Quân đội", shortName: "MBBank", imageId: "58b7190b-a294-4b14-968f-cd365593893e" },
  { bin: "970426", code: "MSB", name: "Ngân hàng TMCP Hàng Hải", shortName: "MSB", imageId: "383ff0f8-d293-4916-98ff-cb88040a74ff" },
  { bin: "970428", code: "NAB", name: "Ngân hàng TMCP Nam Á", shortName: "NamABank", imageId: "3833ba06-b272-4922-8c6d-1ce738bd93fd" },
  { bin: "970419", code: "NCB", name: "Ngân hàng TMCP Quốc Dân", shortName: "NCB", imageId: "b87583b2-bcb5-4625-8f5c-17dac9e5ea61" },
  { bin: "801011", code: "NHB", name: "Ngân hàng Nonghyup - Chi nhánh Hà Nội", shortName: "Nonghyup", imageId: "aadd969d-01bd-4041-8db1-f2e8d8bbd271" },
  { bin: "970448", code: "OCB", name: "Ngân hàng TMCP Phương Đông", shortName: "OCB", imageId: "49d927d2-905d-42e2-a64e-4326da4b729f" },
  { bin: "970414", code: "MBV", name: "Ngân hàng Trách nhiệm hữu hạn Một thành viên Việt Nam Hiện Đại", shortName: "Oceanbank", imageId: "e8e63c02-26a0-493a-ab70-e9e022241b0f" },
  { bin: "970430", code: "PGB", name: "Ngân hàng TMCP Xăng dầu Petrolimex", shortName: "PGBank", imageId: "56f4e977-add6-405f-96a9-4f39d353002e" },
  { bin: "970439", code: "PBVN", name: "Ngân hàng TNHH MTV Public Việt Nam", shortName: "PublicBank", imageId: "f63aac51-4385-4691-ba13-0e515ed1519f" },
  { bin: "970412", code: "PVCB", name: "Ngân hàng TMCP Đại Chúng Việt Nam", shortName: "PVcomBank", imageId: "62fbd4d2-abda-4eb9-b465-9cf33d06037c" },
  { bin: "970403", code: "STB", name: "Ngân hàng TMCP Sài Gòn Thương Tín", shortName: "Sacombank", imageId: "a27bd680-86b4-438c-8ff0-633120c3438a" },
  { bin: "970400", code: "SBC", name: "Ngân hàng TMCP Sài Gòn Công Thương", shortName: "SaigonBank", imageId: "9b05cf95-6544-422d-9976-01d59e52d3b5" },
  { bin: "970429", code: "SCB", name: "Ngân hàng TMCP Sài Gòn", shortName: "SCB", imageId: "699e99a6-cf77-4689-889b-d6c10db3bb37" },
  { bin: "970440", code: "SAB", name: "Ngân hàng TMCP Đông Nam Á", shortName: "SeABank", imageId: "d1838a8e-3f84-4fc2-8168-a18fcb72662b" },
  { bin: "970443", code: "SHB", name: "Ngân hàng TMCP Sài Gòn - Hà Nội", shortName: "SHB", imageId: "b872b705-3d9f-4702-a723-00b008b7ffa7" },
  { bin: "970424", code: "SHINHAN", name: "Ngân hàng TNHH MTV Shinhan Việt Nam", shortName: "ShinhanBank", imageId: "9a05a55c-b946-4513-9e44-b3493b29b25b" },
  { bin: "970410", code: "SCVN", name: "Ngân hàng TNHH MTV Standard Chartered Bank Việt Nam", shortName: "StandardChartered", imageId: "55e37047-6426-4883-8f97-d5c3ede07c36" },
  { bin: "970407", code: "TCB", name: "Ngân hàng TMCP Kỹ thương Việt Nam", shortName: "Techcombank", imageId: "97c7b39e-812c-48b5-8126-16e187cfe91b" },
  { bin: "963388", code: "TIMO", name: "Ngân hàng số Timo by Ban Viet Bank (Timo by Ban Viet Bank)", shortName: "Timo", imageId: "af005597-37a7-463f-831c-564b851de853" },
  { bin: "970423", code: "TPB", name: "Ngân hàng TMCP Tiên Phong", shortName: "TPBank", imageId: "40461b0d-d370-4b3f-974a-b7798a18952e" },
  { bin: "546035", code: "Ubank", name: "TMCP Việt Nam Thịnh Vượng - Ngân hàng số Ubank by VPBank", shortName: "Ubank", imageId: "fc89bc9b-a11e-4bf9-8d90-e3b4db5e41a0" },
  { bin: "970458", code: "UOB", name: "Ngân hàng United Overseas - Chi nhánh TP. Hồ Chí Minh", shortName: "UnitedOverseas", imageId: "247d67c4-b434-4de8-9a63-278de158255c" },
  { bin: "970441", code: "VIB", name: "Ngân hàng TMCP Quốc tế Việt Nam", shortName: "VIB", imageId: "d7fff155-734b-46f1-ba31-d0aef434e1ba" },
  { bin: "970427", code: "VAB", name: "Ngân hàng TMCP Việt Á", shortName: "VietABank", imageId: "5bf5bf2a-53b1-4c0e-959f-f4aa87966d65" },
  { bin: "970433", code: "VBC", name: "Ngân hàng TMCP Việt Nam Thương Tín", shortName: "VietBank", imageId: "8fdc43c4-0cc7-46eb-82c7-a4813dac60d3" },
  { bin: "970454", code: "VCAB", name: "Ngân hàng TMCP Bản Việt", shortName: "VietCapitalBank", imageId: "e6f12053-6283-4359-97b1-28e0cf4141ce" },
  { bin: "970436", code: "VCB", name: "Ngân hàng TMCP Ngoại Thương Việt Nam", shortName: "Vietcombank", imageId: "d0e196fc-3d4c-4501-b453-ac8c3df968cf" },
  { bin: "970415", code: "VTB", name: "Ngân hàng TMCP Công thương Việt Nam", shortName: "Vietinbank", imageId: "22abf9a2-6ede-4e48-8b8a-9c8fc1303c22" },
  { bin: "971005", code: "VTLMONEY", name: "Tổng Công ty Dịch vụ số Viettel - Chi nhánh tập đoàn công nghiệp viễn thông Quân Đội", shortName: "ViettelMoney", imageId: "2cd19eb5-2a5a-4648-8d32-3bd669a4a2ed" },
  { bin: "971011", code: "VNPTMONEY", name: "Trung tâm dịch vụ tài chính số VNPT- Chi nhánh Tổng công ty truyền thông (VNPT Fintech)", shortName: "VNPTMoney", imageId: "b4e8c5b0-0e67-4ea9-a214-1f5f12caf25f" },
  { bin: "970432", code: "VPB", name: "Ngân hàng TMCP Việt Nam Thịnh Vượng", shortName: "VPBank", imageId: "029446c5-396b-49a1-adf2-482f2a45b8e0" },
  { bin: "970421", code: "VRB", name: "Ngân hàng Liên doanh Việt - Nga", shortName: "VRB", imageId: "17c01559-494a-454b-8cda-c8cc4d61e89e" },
  { bin: "970457", code: "WRB", name: "Ngân hàng TNHH MTV Woori Việt Nam", shortName: "Woori", imageId: "2fd79b2b-bb01-48dc-9964-c8455da13a7e" },
];
