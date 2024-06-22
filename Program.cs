using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Configuration;
using System.IO;
using System.Text.Json;
using System.Text.Unicode;
using System.Text.Encodings.Web;

namespace TeacherPreffsCollector
{
    internal static class Program
    {
        static Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
        static string fileDirectory = config.AppSettings.Settings["fileDirectory"].Value; // Использовать для задания папки для импорта/экспорта
        static JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions()
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic),
            WriteIndented = true
        };

        static TeacherPrefsEntities entities = new TeacherPrefsEntities();
        static List<Teacher> teachers = entities.Teacher.ToList();
        static char[] sep = new char[] { ' ', ',' };
        static char[] sepForConsole = new char[] { '$' };
        //static char[] sepForYear = new char[] { ' ', '-' };
        static char[] sepForYear = new char[] { '-' };// Яруллин
        static char[] sepForGroups = new char[] { '%' };// Яруллин

        static string[] stime = { "8:00", "9:40", "11:30", "13:20", "15:00", "16:40", "18:20" };
        static string[] etime = {  "8:00 (Конец занятия: 9:30)",
                                                        "9:40 (Конец занятия: 9:30)",
                                                        "11:30 (Конец занятия: 13:00)",
                                                        "13:20 (Конец занятия: 14:50)",
                                                        "15:00 (Конец занятия: 16:30)",
                                                        "16:40 (Конец занятия: 18:10)",
                                                        "18:20 (Конец занятия: 19:50)"};
        static string[] weekdays = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
        static string[] weekdaysFull = { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };
        static string[] disciplineTypes = { "Лек", "Пр", "Лаб" };
        static List<string> departments = new List<string>();

        static async Task Main(string[] args)
        {
            var groupedAuditories = entities.Auditory.GroupBy(x => x.Department).ToList();
            foreach (var ga in groupedAuditories)
            {
                departments.Add(ga.Key);
            }
            departments = departments.OrderBy(x => x).ToList();

            var botClient = new TelegramBotClient(ConfigurationManager.ConnectionStrings["botKey"].ConnectionString);
            CancellationTokenSource cts = new();

            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            botClient.StartReceiving(updateHandler: HandleUpdateAsync,
                                        pollingErrorHandler: HandlePollingErrorAsync,
                                        receiverOptions: receiverOptions,
                                        cancellationToken: cts.Token);

            var me = await botClient.GetMeAsync();

            Console.WriteLine($"Started listening to @{me.Username}\n\nСписок команд - {getCommandList()}");

            string c = Console.ReadLine();
            while (c != "/exit")
            {
                var cArgs = c.Split(sepForConsole, StringSplitOptions.RemoveEmptyEntries);

                switch (cArgs[0])
                {
                    case "/et":
                        Console.WriteLine(await ExportTeachers());
                        break;

                    case "/ep":
                        Console.WriteLine(await ExportPreferences());
                        break;

                    case "/it":
                        Console.WriteLine(await ImportTeachers());
                        break;

                    case "/id":
                        Console.WriteLine(await ImportDisciplines());
                        break;

                    case "/changeDirectory":
                        if (cArgs.Length == 2)
                        {
                            config.AppSettings.Settings["fileDirectory"].Value = cArgs[1] + "\\";
                            config.Save();
                            ConfigurationManager.RefreshSection("appSettings");
                            fileDirectory = config.AppSettings.Settings["fileDirectory"].Value;
                            Console.WriteLine($"Директория для сохранения изменена на {cArgs[1]}");
                        }
                        else
                        {
                            Console.WriteLine($"Директория для сохранения не указана или использован неверный формат команды");
                        }
                        break;

                    case "/help":
                        Console.WriteLine(getCommandList());
                        break;

                    default:
                        Console.WriteLine("Команда не найдена, список команд - " + getCommandList());
                        break;
                }
                c = Console.ReadLine();
            }
            cts.Cancel();
        }

        static string getPrefInfo(Preference pref)
        {
            string response = "";
            Auditory aud = null;
            if (pref.AuditoryID != null) aud = entities.Auditory.Single(x => x.ID == pref.AuditoryID);

            response += "*" + pref.DisciplineName + "*" +
                "\nТип: " + getDisciplineTypeInfo(pref.DisciplineType) +
                "\nГруппы: " + pref.Groups.Replace("%", ",\n                  ") + " (" + pref.StudentsCount + " чел.)";
            if (pref.Subgroup != 0) response += "\nПодгруппа: " + pref.Subgroup;
            response += "\nОбщее кол-во часов в семестре: ";
            if (pref.Hours % 8 > 4) response += (pref.Hours + 8 - (pref.Hours % 8)).ToString();
            else response += (pref.Hours - (pref.Hours % 8)).ToString();
            response += "\n\n";

            response += "*Ваши требования:*\n";
            if (aud != null) response += "*Аудитория:* " + getAuditoryInfoShort(aud);
            else response += "*Аудитория:* Не задана";

            response += "\n*Частота:* ";
            if (pref.BCFirstWeek != null) response += $"\nКол-во часов:" +
                    $"\n        До смены: " +
                    $"\n                По первым неделям - {pref.BCFirstWeek}" +
                    $"\n                По вторым неделям - {pref.BCSecondWeek}" +
                    $"\n        После смены: " +
                    $"\n                По первым неделям - {pref.ACFirstWeek}" +
                    $"\n                По вторым неделям - {pref.ACSecondWeek}";
            else response += "Не задана";

            response += "\n*Дни недели:* ";
            if (pref.Weekdays != null) response += pref.Weekdays;
            else response += "Не заданы";

            response += "\n*Время:* ";
            if (pref.TimeBegin != null) response += pref.TimeBegin + " - " + pref.TimeEnd;
            else response += "Не задано";

            return response;
        }

        static string getTeacherInfo(Teacher tch)
        {
            string response = "";

            response += "*Ваши требования:*";

            response += "\nДни недели: ";
            if (tch.Weekdays != null) response += tch.Weekdays;
            else response += "Не заданы";

            response += "\nВремя: ";
            if (tch.TimeBegin != null) response += tch.TimeBegin + " - " + tch.TimeEnd;
            else response += "Не задано";

            response += "\nАудитории: ";
            if (tch.AuditoryIDs != null)
            {
                var auIDs = tch.AuditoryIDs.Split(sep, StringSplitOptions.RemoveEmptyEntries).ToList();
                var auList = entities.Auditory.Where(x => auIDs.Contains(x.ID.ToString())).ToList();
                List<Auditory> selectedAuditories = new List<Auditory>();

                foreach (var a in auIDs) selectedAuditories.Add(auList.Single(x => x.ID.ToString() == a));

                foreach (var au in selectedAuditories)
                    response += getAuditoryInfoShort(au) + "\n                         ";
            }
            else response += "Не заданы";

            return response;
        }

        static string getAuditoryInfo(Auditory au)
        {
            return "\n*Аудитория:* \n" +
                   "Корпус: " + au.Department + "\n" +
                   "Номер: " + au.Number + "\n" +
                   "Вместимость: " + au.Capacity + "\n" +
                   "Кол-во рабочих мест (компьютеров): " + au.Workplaces + "\n" +
                   "Оборудование: " + au.Equipment + "\n" +
                   "Проектор: " + getProjectorInfo(au.Projector) + "\n";
        }

        static string getAuditoryInfoShorter(Auditory au)
        {
            string response = $"*Ауд.{au.Number} к.{au.Department}*, {au.Capacity} чел.," +
                $" Кол-во комп. {au.Workplaces}, Проектор: {getProjectorInfo(au.Projector)}";
            if (au.Equipment != null) response += $", {au.Equipment}";
            return response;
        }

        static string getAuditoryInfoShort(Auditory au) 
        {
            return $"{au.Number} к.{au.Department}";
        }

        static string getAuditoryInfoShortest(Auditory au)
        {
            return $"{au.Department}#{au.Number}";
        }

        static string getNextDepartment(int d)
        {
            if (d == departments.Count - 1) return "-1";
            return (d + 1).ToString();
        }

        static string getDepartmentInfo(int d)
        {
            if (d != -1) return departments[d];
            return "";
        }

        static string getNextProjectorCode(int c)
        {
            if (c == -1) return "2";
            return (c - 1).ToString();
        }

        static string getProjectorInfo(int s)
        {
            switch (s)
            {
                case -1: return "";
                case 0: return "Нет";
                case 1: return "Низкое качество";
                case 2: return "Высокое качество";
                default: return "Неправильный код проектора";
            }
        }
        static string getDisciplineTypeInfo(int t)
        {
            return disciplineTypes[t];
        }

        static string getCommandList()
        {
            return "\n/et - Экспортировать данные о преподавателях" +
                   "\n/ep - Экспортировать данные о требованиях" +
                   "\n/it - Импортировать данные о преподавателях" +
                   "\n/id - Импортировать данные о дисциплинах" +
                   "\n/changeDirectory$<ВашаДиректорияДляСохранения> - Изменить директорию для импорта-экспорта данных";
        }

        async static Task<string> ExportPreferences()
        {
            string response;
            if (!Directory.Exists($"{fileDirectory}Export\\Preferences")) Directory.CreateDirectory($"{fileDirectory}Export\\Preferences");
            string fn = fileDirectory + "Export\\Preferences\\Preferences " + DateTime.Now.ToString().Replace(":", "-") + ".json";
            string auInfo = "";

            List<Preference> prefs = entities.Preference.ToList();

            try
            {
                foreach (Preference pref in prefs)
                {
                    if (pref.AuditoryID != null)
                    {
                        auInfo += $"{getAuditoryInfoShortest(entities.Auditory.Single(x => x.ID == pref.AuditoryID))}";
                        pref.AuditoryInfo = auInfo;
                        auInfo = "";
                    }
                }
                using (FileStream f = new FileStream(fn, FileMode.OpenOrCreate))
                {
                    await JsonSerializer.SerializeAsync(f, prefs, jsonSerializerOptions);
                }
                response = $"Данные о требованиях успешно экспортированы, путь к файлу -\n{new FileInfo(fn).Directory.FullName}";
            }
            catch (Exception ex)
            {
                response = ex.Message;
            }
            return response;
        }

        async static Task<string> ExportTeachers()
        {
            string response;
            if (!Directory.Exists($"{fileDirectory}Export\\Teachers")) Directory.CreateDirectory($"{fileDirectory}Export\\Teachers");
            string fn = fileDirectory + "Export\\Teachers\\Teachers " + DateTime.Now.ToString().Replace(":", "-") + ".json";
            string auInfo = "";
            bool moreThanOne = false;

            List<Teacher> tchs = entities.Teacher.ToList();

            try
            {
                foreach (Teacher t in tchs)
                {
                    if (t.AuditoryIDs != null)
                    {
                        moreThanOne = false;
                        foreach (string au in t.AuditoryIDs.Split(sep, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (moreThanOne) auInfo += ", ";
                            auInfo += $"{getAuditoryInfoShortest(entities.Auditory.Single(x => x.ID.ToString() == au))}";
                            moreThanOne = true;
                        }
                        t.AuditoryInfo = auInfo;
                        auInfo = "";
                    }
                }
                using (FileStream f = new FileStream(fn, FileMode.OpenOrCreate))
                {
                    await JsonSerializer.SerializeAsync(f, tchs, jsonSerializerOptions);
                }
                response = $"Данные о преподавателях успешно экспортированы, путь к файлу -\n{new FileInfo(fn).Directory.FullName}";
            }
            catch (Exception ex)
            {
                response = ex.Message;
            }
            return response;
        }

        public static List<Teacher> ParseTeachers(Dictionary<string, ForName> npt)
        {
            List <Teacher> ImportingT = new List<Teacher>();
            Teacher t;
            foreach (KeyValuePair<string, ForName> kvp in npt)
            {
                t = new Teacher();
                t.ID = kvp.Key;
                t.Name = kvp.Value.name;

                ImportingT.Add(t);
            }
            return ImportingT;
        }
        async static Task<string> ImportTeachers()
        {
            string response = "";
            string fn = "";
            fn = fileDirectory + "Import\\Teachers\\teachers.json";
            int updated = 0, added = 0;
            int code = 0;
            bool codeUnique = false;

            List<Teacher> allT = entities.Teacher.ToList();
            List<Teacher> importingT = null;
            Teacher updatingT = null;
            Dictionary<string, ForName> notParsedT = null;
            Random rnd = new Random();

            try
            {
                using (FileStream f = new FileStream(fn, FileMode.OpenOrCreate))
                {
                     notParsedT = await JsonSerializer.DeserializeAsync<Dictionary<string, ForName>>(f);
                }
                importingT = ParseTeachers(notParsedT);
                foreach (Teacher t in importingT)
                {
                    //Console.WriteLine($"ID = \"{t.ID}\" Name = \"{t.Name}\"\n") ;
                    if (allT.Where(x => x.ID == t.ID).Count() == 1) updatingT = entities.Teacher.Single(x => x.ID == t.ID);
                    if (updatingT != null)
                    {
                        updatingT.Name = t.Name;
                        updated++;
                    }
                    else
                    {
                        codeUnique = false;
                        while (!codeUnique)
                        {
                            code = rnd.Next(899999) + 100000;
                            codeUnique = true;
                            foreach (Teacher tch in allT)
                            {
                                if (code == t.IdentificationCode) codeUnique = false;
                            }
                        }
                        t.IdentificationCode = code;
                        entities.Teacher.Add(t);
                        added++;
                    }
                }
                entities.SaveChanges();
                teachers = entities.Teacher.ToList();
                response = $"Данные о преподавателях успешно импортированы\nКоличество добавленных записей - {added}\nКоличество обновленных записей - {updated}";
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException ex)
            {
                response = string.Join("\n", ex.EntityValidationErrors.SelectMany(x => x.ValidationErrors).Select(x => x.PropertyName + ": " + x.ErrorMessage));
            }
            catch (Exception ex)
            {
                response = ex.InnerException.ToString();
            }
            return response;
        }

        public static List<Preference> ParseDisciplines(Dictionary<string, NameAndRecords> npd)
        {
            List<Preference> importingP = new List<Preference>();
            Preference pref;

            foreach (KeyValuePair<string, NameAndRecords> dn in npd)
            {             
                foreach (KeyValuePair<string, DiscInfo> d in dn.Value.records)
                {
                    //Console.WriteLine(d.Value.group + "\n");
                    if ((d.Value.type == 0 || d.Value.type == 1 || d.Value.type == 2) &&
                        !d.Value.group.Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("у") &&
                        !d.Value.group.Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("з"))
                    {
                        foreach (KeyValuePair<string, object[]> t in d.Value.teachers)
                        {
                            pref = new Preference();

                            pref.DisciplineName = dn.Value.name;

                            pref.DisciplineIDs = d.Key;
                            pref.DisciplineType = d.Value.type;
                            pref.Groups = d.Value.group;
                            pref.StudentsCount = d.Value.students;
                            pref.Subgroup = d.Value.subgroup;

                            pref.TeacherID = t.Key;
                            //Console.WriteLine(t.Value[0]);
                            pref.Hours = Convert.ToInt32(t.Value[0].ToString());

                            importingP.Add(pref);
                        }
                    }
                }
            }
            return importingP;
        }

        async static Task<string> ImportDisciplines()
        {
            string response = "";
            string fn = fileDirectory + "Import\\Disciplines\\load.json";
            int addedToStream = 0, added = 0, createdStreams = 0;

            bool contains = false, addingToStream = false, creatingStreamCode = false;

            List<Preference> allP = entities.Preference.ToList();
            List<Auditory> auList = entities.Auditory.ToList();
            List<Teacher> tList = entities.Teacher.ToList();

            List<Preference> importingP = new List<Preference>();
            Dictionary<string, NameAndRecords> notParsedP = null;

            int newStreamCode = 0;
            int? maxStreamCode = allP.Max(x => x.Stream);
            if (maxStreamCode == null) maxStreamCode = 0;

            try
            {
                using (FileStream f = new FileStream(fn, FileMode.OpenOrCreate))
                {
                    notParsedP = await JsonSerializer.DeserializeAsync<Dictionary<string, NameAndRecords>>(f);
                }
                importingP = ParseDisciplines(notParsedP);

                foreach (Preference ip in importingP)
                {
                    contains = false;
                    foreach (Preference ep in allP)
                    {
                        if (ep.DisciplineIDs.Split(sep, StringSplitOptions.RemoveEmptyEntries).Contains(ip.DisciplineIDs))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (!contains)
                    {
                        addingToStream = false;
                        if (ip.DisciplineType == 0)
                        {
                            foreach (Preference ep in allP.Where(x => x.DisciplineType == 0))
                            {
                                //if (ep.TeacherID == ip.TeacherID) Console.WriteLine("1");
                                //Console.WriteLine(ip.Groups + " alo " + ip.Groups.Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0].Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[1]);
                           
                                //if (ep.Groups
                                //            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                //            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[1] ==
                                //        ip.Groups
                                //            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                //            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[1]) Console.WriteLine("2");
                                //if (ep.DisciplineName == ip.DisciplineName) Console.WriteLine("3");
                                //if (!ep.Groups
                                //            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)
                                //            .Contains(ip.Groups)) Console.WriteLine("4");
                                //Console.WriteLine("\n");
                                if (
                                        ep.TeacherID == ip.TeacherID &&

                                        ep.Groups
                                            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0] // Яруллин
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[1]  ==
                                        ip.Groups
                                            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[1] &&

                                        ep.DisciplineName == ip.DisciplineName &&

                                        !ep.Groups
                                            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)
                                            .Contains(ip.Groups) &&

                                            ((ep.Groups
                                    .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("б") &&
                                            ip.Groups
                                            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("б")) ||
                                            (ep.Groups
                                    .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("м") &&
                                            ip.Groups
                                            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("м")) ||
                                            (ep.Groups
                                    .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("с") &&
                                            ip.Groups
                                            .Split(sepForGroups, StringSplitOptions.RemoveEmptyEntries)[0]
                                            .Split(sepForYear, StringSplitOptions.RemoveEmptyEntries)[2].Contains("с")))
                                   )
                                {
                                    addingToStream = true;
                                    creatingStreamCode = false;
                                    if (ep.Stream == null)
                                    {
                                        newStreamCode = (int)maxStreamCode + 1;
                                        maxStreamCode++;
                                        creatingStreamCode = true;
                                        createdStreams++;
                                    }

                                    ep.DisciplineIDs += $", {ip.DisciplineIDs}";
                                    //ep.Groups += $", {ip.Groups}"; // Яруллин
                                    ep.Groups += $"%{ip.Groups}"; 
                                    ep.StudentsCount += ip.StudentsCount;
                                    if (creatingStreamCode) ep.Stream = newStreamCode;

                                    addedToStream++;
                                    break;
                                }
                            }
                        }
                        if (!addingToStream)
                        {
                            entities.Preference.Add(ip);
                            allP.Add(ip);
                            added++;
                        }
                    }
                }

                Auditory aud;
                foreach (Teacher t in tList)
                {
                    foreach (var p in allP.Where(x => x.TeacherID == t.ID).ToList())
                    {
                        if (t.AuditoryIDs != null)
                        {
                            string[] sa = t.AuditoryIDs.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < sa.Length; i++)
                            {
                                aud = auList.Single(x => x.ID.ToString() == sa[i]);

                                if (aud.Capacity + aud.Capacity / 10 >= p.StudentsCount)
                                {
                                    p.AuditoryID = Convert.ToInt32(sa[i]);
                                    break;
                                }
                            }
                        }
                        if (t.TimeBegin != null && t.TimeEnd != null)
                        {
                            p.TimeBegin = t.TimeBegin;
                            p.TimeEnd = t.TimeEnd;
                        }
                        if (t.Weekdays != null)
                        {
                            p.Weekdays = t.Weekdays;
                        }
                    }
                }

                entities.SaveChanges();
                response = $"Данные о дисциплинах успешно импортированы\nКоличество добавленных записей - {added}\nКоличество записей, добавленных в потоки - {addedToStream}\nКоличество созданных потоков - {createdStreams}";
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException ex)
            {
                response = string.Join("\n", ex.EntityValidationErrors.SelectMany(x => x.ValidationErrors).Select(x => x.PropertyName + ": " + x.ErrorMessage));
            }
            catch (Exception ex)
            {
                response = ex.Message + "\n@\n" + ex.StackTrace;
            }
            return response;
        }

        async static Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Message is not null)
                {
                    Message sentMessage;
                    var message = update.Message;

                    if (message.Text is not null)
                    {
                        var messageText = message.Text;
                        var chatId = message.Chat.Id;

                        Preference pref = null;
                        Teacher tch = null;
                        List<InlineKeyboardButton[]> ikb = new List<InlineKeyboardButton[]>();

                        foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == chatId) tch = t;
                        string response = "";
                        List<string> splittedResponse = new List<string>();
                        var chunkSize = 3500;

                        string[] cArgs = messageText.Split('Q');

                        if (tch != null)
                        {
                            Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");
                            switch (cArgs[0])
                            {
                                case "/disciplines":
                                    response += "Ваш список дисциплин:\n";
                                    foreach (Preference pf in entities.Preference.Where(x => x.TeacherID == tch.ID).OrderBy(x => x.Groups.Substring(x.Groups.Length - 1)).ThenBy(x => x.DisciplineName).ThenBy(x => x.DisciplineType).ThenBy(x => x.Groups).ThenBy(x => x.Subgroup).ToList())
                                    {
                                        string thisString = getPrefInfo(pf)
                                              + "\nИзменить требования: /preferencesQ" + pf.ID + "\n\n\n\n";
                                        if (splittedResponse.Count == 0 || splittedResponse[splittedResponse.Count - 1].Length > chunkSize) splittedResponse.Add(thisString);
                                        else
                                        {
                                            splittedResponse[splittedResponse.Count - 1] += thisString;
                                        }
                                    }
                                    break;

                                case "/preferences":
                                    if (cArgs.Length == 1)
                                    {
                                        response =
                                            $"Вы можете задать аудитории, дни недели и время занятий \"по умолчанию\", " +
                                            $"эти значения будут применены для всех ваших дисциплин, " +
                                            $"но Вы всегда сможете изменить их для каждой дисциплины по отдельности, используя /disciplines\n\n" +
                                            getTeacherInfo(tch) +
                                            $"\n\nЧто Вы хотите изменить?";
                                        ikb.Add(new[]
                                                {
                                                    InlineKeyboardButton.WithCallbackData(text: "Аудитории", callbackData: $"Edit$Auditory$-1"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Дни недели", callbackData: $"Edit$Weekdays$-1"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Время", callbackData: $"Edit$Time$-1")
                                                });
                                    }
                                    if (cArgs.Length == 2)
                                    {
                                        string pfID = cArgs[1];
                                        try
                                        {
                                            pref = entities.Preference.Single(x => x.ID.ToString() == pfID);
                                            if (tch.ID == pref.TeacherID)
                                            {
                                                response =
                                                    "*Вы редактируете информацию о следующей дисциплине:*\n"
                                                    + getPrefInfo(pref)
                                                    + "\n\nЧто Вы хотите изменить?";
                                                ikb.Add(new[]
                                                {
                                                    InlineKeyboardButton.WithCallbackData(text: "Аудиторию", callbackData: $"Edit$Auditory${pfID}"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Частоту", callbackData: $"Edit$Frequency${pfID}"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Дни недели", callbackData: $"Edit$Weekdays${pfID}"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Время", callbackData: $"Edit$Time${pfID}")
                                                });

                                            }
                                            else
                                            {
                                                response = "Вы не имеете доступа к этой дисциплине.";
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            response = "Дисциплина не найдена.";
                                        }
                                    }
                                    break;

                                case "/help":
                                    response = "Полный список команд:" +
                                    "\n/preferences - Задать требования \"по умолчанию\"" +
                                    "\n/disciplines - Получить список ваших дисциплин и требований" +
                                    "\n/logout - Выйти из аккаунта";
                                    break;

                                case "/logout":
                                    foreach (var t in teachers)
                                    {
                                        if (t.ChatID == chatId.ToString())
                                        {
                                            t.ChatID = null;
                                            entities.SaveChanges();
                                        }
                                    }
                                    response = "Вы успешно вышли из аккаунта.";
                                    break;

                                default:
                                    response = "Команда не найдена: " + messageText;
                                    break;
                            }
                        }
                        else
                        {
                            if (messageText == "/start")
                            {
                                response = "Здравствуйте, перед началом работы необходимо авторизоваться, введите код авторизации.";
                            }
                            else
                            {
                                Teacher newTeacher = new Teacher();
                                bool found = false;
                                foreach (var t in teachers)
                                {
                                    if (t.IdentificationCode.ToString() == messageText)
                                    {
                                        t.ChatID = chatId.ToString();
                                        newTeacher = t;
                                        entities.SaveChanges();
                                        found = true;
                                    }
                                }

                                if (found)
                                {
                                    response = $"Вы успешно авторизовались, {newTeacher.Name}.\nДля работы с ботом используйте меню, чтобы получить список команд используйте /help.";
                                }
                                else
                                {
                                    response = "Неверный код, попробуйте снова.";
                                }
                            }
                        }
                        InlineKeyboardMarkup rmp = new InlineKeyboardMarkup(ikb);

                        if (splittedResponse.Count == 0) splittedResponse.Add(response);
                        for (int i = 0; i < splittedResponse.Count - 1; i++)
                        {
                            sentMessage = await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: splittedResponse[i],
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                        }
                        sentMessage = await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: splittedResponse[splittedResponse.Count - 1],
                            parseMode: ParseMode.Markdown,
                            replyMarkup: rmp,
                            cancellationToken: cancellationToken);
                    }
                }
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                if (update.CallbackQuery is not null)
                {
                    if (update.CallbackQuery.Data == "doNothing")
                    {
                        await botClient.AnswerCallbackQueryAsync(callbackQueryId: update.CallbackQuery.Id, text: "");
                        return;
                    }
                    var callbackQuery = update.CallbackQuery;
                    var inlineMessageId = callbackQuery.InlineMessageId;
                    string[] cArgs = callbackQuery.Data.Split('$');

                    int pfID = Convert.ToInt32(cArgs[2]);
                    string response = "";
                    List<string> splittedResponse = new List<string>();

                    int i = 0;
                    bool clear = false;
                    bool updateInfo = false;
                    string noti = "";                

                    Console.WriteLine($"Pressed inline button, Data = {callbackQuery.Data}");

                    List<Auditory> auList = null;
                    string option = "";

                    InlineKeyboardButton[] bRow = new InlineKeyboardButton[3];
                    List<InlineKeyboardButton[]> ikb = new List<InlineKeyboardButton[]>();
                    Preference pref = null;
                    Teacher tch = null;
                    Auditory aud = null;

                    switch (cArgs[0])
                    {
                        case "Edit":
                            switch (cArgs[1])
                            {
                                case "Auditory":
                                    //splittedResponse.Add(callbackQuery.Message.Text.Replace("Что Вы хотите изменить?", "*Выбор аудитории:*"));
                                    //response = callbackQuery.Message.Text.Replace("Что Вы хотите изменить?", "*Выбор аудитории:*");
                                    
                                    if (pfID != -1)
                                    {
                                        response = callbackQuery.Message.Text.Replace("Что Вы хотите изменить?", "*Выбор аудитории:*") + "\n";
                                        pref = entities.Preference.Single(x => x.ID == pfID);
                                        auList = entities.Auditory.Where(x => x.Capacity + x.Capacity / 10 >= pref.StudentsCount).OrderBy(x => x.Department).ThenBy(x => x.Number).ToList();

                                        i = 0;
                                        foreach (var au in auList)
                                        {
                                            //thisString = getAuditoryInfo(au);
                                            //if (splittedResponse.Count == 0 || splittedResponse[splittedResponse.Count - 1].Length > chunkSize) splittedResponse.Add(thisString);
                                            //else
                                            //{
                                            //    splittedResponse[splittedResponse.Count - 1] += thisString;
                                            //}
                                            response += getAuditoryInfoShorter(au) + "\n\n";
                                            if (pref.AuditoryID == au.ID) option = "✅ ";

                                            bRow[i] = InlineKeyboardButton.WithCallbackData(text: $"{option} {getAuditoryInfoShort(au)}",
                                                callbackData: $"Choose$Auditory${pfID}${au.ID}");
                                            i++;
                                            if (i == 3)
                                            {
                                                ikb.Add(bRow);
                                                i = 0;
                                                bRow = new InlineKeyboardButton[3];
                                            }
                                            
                                            option = "";
                                        }
                                        if (i != 0)
                                        {
                                            while (i != 3)
                                            {
                                                bRow[i] = InlineKeyboardButton.WithCallbackData(text: $" ",
                                                  callbackData: $"doNothing");
                                                i++;
                                            }
                                            ikb.Add(bRow);
                                        }
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"Проектор:",
                                                callbackData: $"Sort$Auditory${pfID}${2}${-1}"),
                                                   InlineKeyboardButton.WithCallbackData(text: $"Корпус:",
                                                callbackData: $"Sort$Auditory${pfID}${-1}${0}")});

                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Auditory${pfID}"),
                                                   InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Auditory${pfID}$")});
                                    }
                                    else
                                    {
                                        response = "*Выбор аудитории:*\n";
                                        auList = entities.Auditory.ToList();
                                        List<string> selectedAuditoryIDs = new List<string>();
                                        List<Auditory> selectedAuditories = new List<Auditory>();

                                        int p = -1;
                                        int k = -1;

                                        if (cArgs.Length == 3)
                                        {
                                            foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == callbackQuery.Message.Chat.Id) tch = t;
                                            if (tch.AuditoryIDs != null) selectedAuditoryIDs = tch.AuditoryIDs.ToString().Split(sep, StringSplitOptions.RemoveEmptyEntries).ToList();
                                        }

                                        if (cArgs.Length == 7)
                                        {
                                            //response = response
                                            //    .Remove(response.IndexOf("Выбор аудитории:") + 16, response.Length - response.IndexOf("Выбор аудитории:") - 16)
                                            //    .Replace("Выбор аудитории:", "*Выбор аудитории:*");

                                            selectedAuditoryIDs = cArgs[3].Split(sep, StringSplitOptions.RemoveEmptyEntries).ToList();
                                            selectedAuditoryIDs.Add(cArgs[4]);

                                            for (i = 0; i < selectedAuditoryIDs.Count - 1; i++)
                                                if (cArgs[4] == selectedAuditoryIDs[i])
                                                    selectedAuditoryIDs.RemoveAll(x => x == cArgs[4]);
                                            if (selectedAuditoryIDs.Count > 8)
                                            {
                                                await botClient.AnswerCallbackQueryAsync(callbackQueryId: callbackQuery.Id, text: "Нельзя добавить больше 8 аудиторий.");
                                                return;
                                            }
                                        }

                                        bool g = false;
                                        string auIDs = "";
                                        foreach (var au in selectedAuditoryIDs) selectedAuditories.Add(auList.Single(x => x.ID.ToString() == au));
                                        //selectedAuditories = auList.Where(x => selectedAuditoryIDs.Contains(x.ID.ToString())).ToList();

                                        for (i = 0; i < selectedAuditories.Count; i++)
                                        {
                                            if (g)
                                            {
                                                auIDs += ", ";
                                            }
                                            auIDs += selectedAuditoryIDs[i];
                                            g = true;
                                        }
                                        //response += "\n\n\n*Порядок выбора аудиторий определяет приоритет при назначении аудитории для занятия.* " +
                                        //            "Первая выбранная аудитория имеет наибольший приоритет, " +
                                        //            "вторая будет следующей по предпочтительности и т. д.\n" +
                                        //            "Чтобы автоматическое заполнение пожеланий для дисциплин работало лучше, " +
                                        //            "*в первую очередь указывайте аудитории для практик и лабораторных, " +
                                        //            "потом только аудитории для лекций*\n" +
                                        //            "Обратите внимание, что если выбранная Вами аудитория не подходит по вместимости для занятия, " +
                                        //            "то будет выбрана следующая по приоритетности подходящая аудитория.\n\n\n*Выбранные аудитории:*";
                                        //for (i = 0; i < selectedAuditories.Count; i++) response += getAuditoryInfoShortest(selectedAuditories[i]);
                                        if (cArgs.Length == 7)
                                        {
                                            if (cArgs[5] != "-1")
                                            {
                                                p = Convert.ToInt32(cArgs[5]);
                                                auList = auList.Where(x => x.Projector == p).ToList();

                                            }
                                            if (cArgs[6] != "-1")
                                            {
                                                k = Convert.ToInt32(cArgs[6]);
                                                auList = auList.Where(x => x.Department == getDepartmentInfo(k)).ToList();

                                            }
                                        }
                                        response += "*Список всех аудиторий:*\n";
                                        auList = auList.OrderBy(x => x.Department).ThenBy(x => x.Number).ToList();

                                        i = 0;                                       
                                        foreach (var au in auList)
                                        {
                                            response += getAuditoryInfoShorter(au) + "\n\n";
                                            if (selectedAuditoryIDs.Contains(au.ID.ToString())) option = "➖";
                                            else option = "➕";

                                            bRow[i] = InlineKeyboardButton.WithCallbackData(text: $"{option} {getAuditoryInfoShort(au)}",
                                                callbackData: $"Edit$Auditory${pfID}${auIDs}${au.ID}${p}${k}");
                                            i++;
                                            if (i == 3)
                                            {
                                                ikb.Add(bRow);
                                                i = 0;
                                                bRow = new InlineKeyboardButton[3];
                                            }
                                        }
                                        if (i != 0)
                                        {
                                            while (i != 3)
                                            {
                                                bRow[i] = InlineKeyboardButton.WithCallbackData(text: $" ",
                                                  callbackData: $"doNothing");
                                                i++;
                                            }
                                            ikb.Add(bRow);
                                        }

                                        response += "\n*Порядок выбора аудиторий определяет приоритет при назначении аудитории для занятия.* " +
                                        "Первая выбранная аудитория имеет наибольший приоритет, " +
                                        "вторая будет следующей по предпочтительности и т. д.\n" +
                                        "*В первую очередь указывайте аудитории для практик и лабораторных, " +
                                        "потом только аудитории для лекций*\n" +
                                        "Если выбранная Вами аудитория не подходит по вместимости для занятия, " +
                                        "то будет выбрана следующая по приоритетности подходящая аудитория.\n\n\n*Выбранные аудитории:*\n";
                                        
                                        //response += "\n\n\n*Выбранные аудитории:*\n";
                                        g = false;
                                        for (i = 0; i < selectedAuditories.Count; i++)
                                        {
                                            if (g)
                                            {
                                                response += ", ";
                                            }
                                            response += getAuditoryInfoShort(selectedAuditories[i]);
                                            g = true;
                                        }

                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"Проектор: {getProjectorInfo(p)}",
                                                callbackData: $"Sort$Auditory${pfID}${getNextProjectorCode(p)}${k}${auIDs}"),
                                                   InlineKeyboardButton.WithCallbackData(text: $"Корпус: {getDepartmentInfo(k)}",
                                                callbackData: $"Sort$Auditory${pfID}${p}${getNextDepartment(k)}${auIDs}")});
                                        ikb.Add(new[] {
                                        InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Auditory${pfID}"),
                                        InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Auditory${pfID}$"),
                                        InlineKeyboardButton.WithCallbackData(text: "Сохранить",
                                                callbackData: $"Choose$Auditory${pfID}${auIDs}")});
                                    }
                                    break;

                                case "Frequency":
                                    pref = entities.Preference.Single(x => x.ID == pfID);
                                    int hours;
                                    if (pref.Hours % 8 > 4) hours = pref.Hours + 8 - (pref.Hours % 8);
                                    else hours = pref.Hours - (pref.Hours % 8);

                                    int hBefore = 0, hAfter = 0, hPerWeek = 0, hPerFWeek = 0, hPerSWeek = 0;
                                    int a = 0, b = 0;

                                    hAfter = (hours - hours % 16) / 2;
                                    if (hours % 16 < 8) hBefore = hAfter;
                                    else hBefore = hours - hAfter;
                                    if (pref.DisciplineType != 0) (hBefore, hAfter) = (hAfter, hBefore); //swap часов для практик и лаб

                                    response += callbackQuery.Message.Text
                                        .Replace("Выбор частоты:", "*Выбор частоты:*")
                                        .Replace("Что Вы хотите изменить?", $"*Выбор частоты:*\nУ этой дисциплины всего {hours} часов за семестр.")
                                        .Replace("Выберите распределение на первую половину семестра:", "")
                                        .Replace("Выберите распределение на вторую половину семестра:", "")
                                        .Replace("      Выберите количество часов по первым неделям:", "")
                                        .Replace("      Выберите количество часов по вторым неделям:", "");

                                    if (response.IndexOf("часов за семестр.") > 0) response = response.Remove(response.IndexOf("часов за семестр.") + 17, response.Length - response.IndexOf("часов за семестр.") - 17);

                                    if (cArgs.Length == 3)
                                    {
                                        response += $"\n\n*Выберите распределение на семестр:*" +
                                                    $"\n1. До и после смены расписания (Кол-во часов до смены: {hBefore}, Кол-во часов после смены: {hAfter})" +
                                                    $"\n\n2. Только до смены расписания" +
                                                    $"\n\n3. Только после смены расписания";

                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Edit$Frequency${pfID}$1")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Edit$Frequency${pfID}$2")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"3",
                                                callbackData: $"Edit$Frequency${pfID}$3")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"Редактировать вручную",
                                                callbackData: $"Edit$Frequency${pfID}$1${hours}$")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Frequency${pfID}"),
                                                       InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Frequency${pfID}$")});
                                    }

                                    if (cArgs.Length == 4)
                                    {
                                        switch (cArgs[3])
                                        {
                                            case "1":
                                                hPerWeek = hBefore / 8;
                                                response += "\n\n*Выберите распределение на первую половину семестра:*";
                                                break;

                                            case "2":
                                                hPerWeek = hours / 8;
                                                response += "\n\n*Выберите распределение на первую половину семестра:*";
                                                break;

                                            case "3":
                                                hPerWeek = hours / 8;
                                                response += "\n\n*Выберите распределение на вторую половину семестра:*";
                                                break;
                                        }

                                        if (hPerWeek % 2 != 0)
                                        {
                                            hPerFWeek = hPerWeek + 1;
                                            hPerSWeek = hPerWeek - 1;
                                            if (pref.ID % 2 == 0) (hPerFWeek, hPerSWeek) = (hPerSWeek, hPerFWeek); //swap часов для четных занятий
                                        }
                                        else
                                        {
                                            hPerFWeek = hPerWeek;
                                            hPerSWeek = hPerWeek;
                                        }
                                        a = hPerWeek * 2;
                                        if (pref.ID % 2 == 0) (a, b) = (b, a); //swap часов для четных занятий
                                        response += $"\n1. Равномерно (Кол-во часов: по первым неделям - {hPerFWeek}, по вторым неделям - {hPerSWeek})" +
                                                    $"\n\n2. Только по одной неделе (Кол-во часов - {hPerWeek * 2})";
                                        switch (cArgs[3])
                                        {
                                            case "1":
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Edit$Frequency${pfID}${cArgs[3]}${hPerFWeek}-{hPerSWeek}")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Edit$Frequency${pfID}${cArgs[3]}${a}-{b}")});
                                                break;

                                            case "2":
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Choose$Frequency${pfID}${hPerFWeek}-{hPerSWeek}-0-0")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Choose$Frequency${pfID}${a}-{b}-0-0")});
                                                break;

                                            case "3":
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Choose$Frequency${pfID}$0-0-{hPerFWeek}-{hPerSWeek}")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Choose$Frequency${pfID}$0-0-{a}-{b}")});
                                                break;
                                        }
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Edit$Frequency${pfID}")});
                                    }

                                    if (cArgs.Length == 5)
                                    {
                                        hPerWeek = hAfter / 8;
                                        if (hPerWeek % 2 != 0)
                                        {
                                            hPerFWeek = hPerWeek + 1;
                                            hPerSWeek = hPerWeek - 1;
                                            if (pref.ID % 2 == 0) (hPerFWeek, hPerSWeek) = (hPerSWeek, hPerFWeek); //swap часов для четных занятий
                                        }
                                        else
                                        {
                                            hPerFWeek = hPerWeek;
                                            hPerSWeek = hPerWeek;
                                        }
                                        a = hPerWeek * 2;
                                        if (pref.ID % 2 == 0) (a, b) = (b, a); //swap часов для четных занятий

                                        response += $"\n\n*Выберите распределение на вторую половину семестра:*" +
                                                    $"\n1. Равномерно (Кол-во часов: по первым неделям - {hPerFWeek}, по вторым неделям - {hPerSWeek})" +
                                                    $"\n\n2. Только по одной неделе (Кол-во часов - {hPerWeek * 2})";

                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Choose$Frequency${pfID}${cArgs[4]}-{hPerFWeek}-{hPerSWeek}")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Choose$Frequency${pfID}${cArgs[4]}-{a}-{b}")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Edit$Frequency${pfID}$1")});
                                    }

                                    if (cArgs.Length == 6)
                                    {
                                        string[] h = cArgs[5].Split('-');
                                        string filler = "";

                                        int period = Convert.ToInt32(cArgs[3]);
                                        int hoursLeft = Convert.ToInt32(cArgs[4]);

                                        switch (cArgs[3])
                                        {
                                            case "1":
                                                response += $"\nОсталось нераспределенных часов: *{hoursLeft}*" +
                                                            $"\n\n*Выберите распределение на первую половину семестра:*" +
                                                            $"\n      Выберите количество часов по первым неделям:";
                                                filler = "-0-0-0";
                                                break;
                                            case "2":
                                                response += $"\nТекущее распределение:" +
                                                            $"\n        До смены: " +
                                                            $"\n                По первым неделям - {h[0]}" +
                                                            $"\nОсталось нераспределенных часов: *{hoursLeft}*" +
                                                            $"\n\n*Выберите распределение на первую половину семестра:*" +
                                                            $"\n      Выберите количество часов по вторым неделям:";
                                                filler = "-0-0";
                                                break;
                                            case "3":
                                                response += $"\nТекущее распределение:" +
                                                            $"\n        До смены: " +
                                                            $"\n                По первым неделям - {h[0]}" +
                                                            $"\n                По вторым неделям - {h[1]}" +
                                                            $"\nОсталось нераспределенных часов: *{hoursLeft}*" +
                                                            $"\n\n*Выберите распределение на вторую половину семестра:*" +
                                                            $"\n      Выберите количество часов по первым неделям:";
                                                filler = "-0";
                                                break;
                                            case "4":
                                                response += $"\nТекущее распределение:" +
                                                            $"\n        До смены: " +
                                                            $"\n                По первым неделям - {h[0]}" +
                                                            $"\n                По вторым неделям - {h[1]}" +
                                                            $"\n        После смены: " +
                                                            $"\n                По первым неделям - {h[2]}" +
                                                            $"\nОсталось нераспределенных часов: *{hoursLeft}*" +
                                                            $"\n\n*Выберите распределение на вторую половину семестра:*" +
                                                            $"\n      Выберите количество часов по вторым неделям:";
                                                break;
                                        }
                                        if (period != 4)
                                        {
                                            for (i = 0; i * 4 < hoursLeft; i += 2)
                                            {
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"{i}",
                                                callbackData: $"Edit$Frequency${pfID}${period + 1}${hoursLeft - i * 4}${cArgs[5]}{i}-")});
                                            }
                                            ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"{i} (Распределить все оставшиеся часы)",
                                                callbackData: $"Choose$Frequency${pfID}${cArgs[5]}{i}{filler}")});
                                        }
                                        else
                                        {
                                            ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"{hoursLeft / 4} (Распределить все оставшиеся часы)",
                                                callbackData: $"Choose$Frequency${pfID}${cArgs[5]}{hoursLeft / 4}")});
                                        }
                                        if (period != 1)
                                            ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Edit$Frequency${pfID}${period - 1}${hoursLeft + Convert.ToInt32(h[period - 2]) * 4}${cArgs[5].Remove(cArgs[5].Length - 2, 2)}")});
                                        else
                                            ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Edit$Frequency${pfID}")});

                                    }
                                    break;

                                case "Time":
                                    if (cArgs.Length == 3)
                                    {
                                        response += callbackQuery.Message.Text
                                            .Replace("Выбор времени:", "*Выбор времени:*")
                                            .Replace("Что Вы хотите изменить?", "*Выбор времени:\n*")
                                            .Replace("Выберите конец удобного Вам промежутка времени занятий:", "");
                                        if (response.IndexOf("Выбранное") >= 0) response = response.Remove(response.IndexOf("Выбранное"), response.Length - response.IndexOf("Выбранное"));
                                        response += "Выберите *начало* удобного Вам промежутка времени занятий:";
                                        foreach (var t in stime)
                                        {
                                            ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: t,
                                                callbackData: $"Edit$Time${pfID}${i}")});
                                            i++;
                                        }
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Time${pfID}"),
                                                       InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Time${pfID}$")});
                                    }
                                    if (cArgs.Length == 4)
                                    {
                                        int beg = Convert.ToInt32(cArgs[3]);
                                        response += callbackQuery.Message.Text
                                            .Replace("начало", "*конец*")
                                            .Replace("Выбор времени:", $"*Выбор времени:*\nВыбранное Вами начало: {stime[beg]}");

                                        for (i = beg; i < etime.Length; i++)
                                        {
                                            ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: etime[i],
                                                callbackData: $"Choose$Time${pfID}${beg}${i}")});
                                        }
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Edit$Time${pfID}")});
                                    }
                                    break;

                                case "Weekdays":
                                    response += callbackQuery.Message.Text
                                            .Replace("Что Вы хотите изменить?", "*Выбор дней недели:\n*Выбранные дни:\n");
                                    string[] sw = new string[0];
                                    bool[] selectedWeekdays = { false, false, false, false, false, false };

                                    if (cArgs.Length == 3)
                                    {
                                        if (pfID != -1)
                                        {
                                            pref = entities.Preference.Single(x => x.ID == pfID);
                                            if (pref.Weekdays != null) sw = pref.Weekdays.ToString().Split(sep, StringSplitOptions.RemoveEmptyEntries);
                                        }
                                        else
                                        {
                                            foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == callbackQuery.Message.Chat.Id) tch = t;
                                            if (tch.Weekdays != null) sw = tch.Weekdays.ToString().Split(sep, StringSplitOptions.RemoveEmptyEntries);
                                        }
                                        foreach (var w in sw) selectedWeekdays[Array.IndexOf(weekdays, w)] = true;
                                    }
                                    if (cArgs.Length == 4)
                                    {
                                        response = response
                                            .Remove(response.IndexOf("Выбранные дни:") + 14, response.Length - response.IndexOf("Выбранные дни:") - 14)
                                            .Replace("Выбор дней недели:", "*Выбор дней недели:*") + '\n';

                                        sw = cArgs[3].Split(sep, StringSplitOptions.RemoveEmptyEntries);
                                        foreach (var w in sw) selectedWeekdays[Array.IndexOf(weekdays, w)] = !selectedWeekdays[Array.IndexOf(weekdays, w)];
                                    }

                                    bool f = false;
                                    string wd = "";
                                    for (i = 0; i < selectedWeekdays.Length; i++)
                                    {
                                        if (selectedWeekdays[i])
                                        {
                                            if (f)
                                            {
                                                response += ", ";
                                                wd += ", ";
                                            }
                                            response += "*" + weekdaysFull[i] + "*";
                                            wd += weekdays[i];
                                            f = true;
                                        }
                                    }

                                    for (i = 0; i < weekdays.Length; i++)
                                    {
                                        if (selectedWeekdays[i]) option = "➖ ";
                                        else option = "➕ ";
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: option + weekdays[i],
                                                callbackData: $"Edit$Weekdays${pfID}${wd}, {weekdays[i]}")});
                                    }
                                    ikb.Add(new[] {
                                        InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Weekdays${pfID}"),
                                        InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Weekdays${pfID}$"),
                                        InlineKeyboardButton.WithCallbackData(text: "Сохранить",
                                                callbackData: $"Choose$Weekdays${pfID}${wd}")});
                                    break;
                            }
                            break;

                        case "Choose":
                            if (pfID != -1)
                            {
                                pref = entities.Preference.Single(x => x.ID == pfID);
                                switch (cArgs[1])
                                {
                                    case "Auditory":
                                        if (cArgs[3] != "") pref.AuditoryID = Convert.ToInt32(cArgs[3]);
                                        else pref.AuditoryID = null;
                                        break;

                                    case "Frequency":
                                        if (cArgs[3] != "")
                                        {
                                            string[] h = cArgs[3].Split('-');
                                            pref.BCFirstWeek = Convert.ToInt16(h[0]);
                                            pref.BCSecondWeek = Convert.ToInt16(h[1]);
                                            pref.ACFirstWeek = Convert.ToInt16(h[2]);
                                            pref.ACSecondWeek = Convert.ToInt16(h[3]);
                                        }
                                        else
                                        {
                                            pref.BCFirstWeek = null;
                                            pref.BCSecondWeek = null;
                                            pref.ACFirstWeek = null;
                                            pref.ACSecondWeek = null;
                                        }
                                        break;

                                    case "Time":
                                        if (cArgs[3] != "" && cArgs[4] != "")
                                        {
                                            pref.TimeBegin = stime[Convert.ToInt32(cArgs[3])];
                                            pref.TimeEnd = stime[Convert.ToInt32(cArgs[4])];
                                        }
                                        else
                                        {
                                            pref.TimeBegin = null;
                                            pref.TimeEnd = null;
                                        }
                                        break;

                                    case "Weekdays":
                                        if (cArgs[3] != "") pref.Weekdays = cArgs[3];
                                        else pref.Weekdays = null;
                                        break;
                                }
                            }
                            else
                            {
                                foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == callbackQuery.Message.Chat.Id) tch = t;
                                switch (cArgs[1])
                                {
                                    case "Auditory":
                                        if (cArgs[3] != "")
                                        {
                                            tch.AuditoryIDs = cArgs[3];
                                            string[] sa = cArgs[3].Split(sep, StringSplitOptions.RemoveEmptyEntries);
                                            auList = entities.Auditory.ToList();

                                            foreach (var p in entities.Preference.Where(x => x.TeacherID == tch.ID).ToList())
                                            {
                                                for (i = 0; i < sa.Length; i++)
                                                {
                                                    aud = auList.Single(x => x.ID.ToString() == sa[i]);

                                                    if (aud.Capacity >= p.StudentsCount)
                                                    {
                                                        p.AuditoryID = Convert.ToInt32(sa[i]);
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        else tch.AuditoryIDs = null;
                                        break;
                                    case "Time":
                                        if (cArgs[3] != "" && cArgs[4] != "")
                                        {
                                            tch.TimeBegin = stime[Convert.ToInt32(cArgs[3])];
                                            tch.TimeEnd = stime[Convert.ToInt32(cArgs[4])];
                                            foreach (var p in entities.Preference.Where(x => x.TeacherID == tch.ID).ToList())
                                            {
                                                p.TimeBegin = stime[Convert.ToInt32(cArgs[3])];
                                                p.TimeEnd = stime[Convert.ToInt32(cArgs[4])];
                                            }
                                        }
                                        else
                                        {
                                            tch.TimeBegin = null;
                                            tch.TimeEnd = null;
                                        }
                                        break;

                                    case "Weekdays":
                                        if (cArgs[3] != "")
                                        {
                                            tch.Weekdays = cArgs[3];
                                            foreach (var p in entities.Preference.Where(x => x.TeacherID == tch.ID).ToList())
                                            {
                                                p.Weekdays = cArgs[3];
                                            }
                                        }
                                        else tch.Weekdays = null;
                                        break;
                                }
                            }
                            updateInfo = true;
                            entities.SaveChanges();
                            noti = "Данные успешно сохранены.";
                            break;

                        case "Sort":
                            switch (cArgs[1])
                            {
                                case "Auditory":
                                    response = callbackQuery.Message.Text;
                                    string fifthArg = "";
                                    List<Auditory> allAu = entities.Auditory.ToList();

                                    if (pfID != -1)
                                    {
                                        pref = entities.Preference.Single(x => x.ID == pfID);
                                        auList = allAu.Where(x => x.Capacity + x.Capacity / 10 >= pref.StudentsCount).ToList();
                                    }
                                    else
                                    {
                                        auList = allAu;
                                    }

                                    if (cArgs[3] != "-1") auList = auList.Where(x => x.Projector.ToString() == cArgs[3]).ToList();
                                    if (cArgs[4] != "-1") auList = auList.Where(x => x.Department == getDepartmentInfo(Convert.ToInt32(cArgs[4]))).ToList();

                                    auList = auList.OrderBy(x => x.Department).ThenBy(x => x.Number).ToList();

                                    if (pfID != -1)
                                    {
                                        response = response
                                            .Remove(response.IndexOf("Выбор аудитории:") + 16, response.Length - response.IndexOf("Выбор аудитории:") - 16)
                                            .Replace("Выбор аудитории:", "*Выбор аудитории:*") + "\n";
                                        if (auList.Count() == 0) response += "\nПо заданным фильтрам аудиторий не найдено.";
                                        i = 0;
                                        foreach (var au in auList)
                                        {
                                            response += getAuditoryInfoShorter(au) + "\n\n";
                                            if (pref.AuditoryID == au.ID) option = "✅ ";

                                            bRow[i] = InlineKeyboardButton.WithCallbackData(text: $"{option} {getAuditoryInfoShort(au)}",
                                                callbackData: $"Choose$Auditory${pfID}${au.ID}");
                                            i++;
                                            if (i == 3)
                                            {
                                                ikb.Add(bRow);
                                                i = 0;
                                                bRow = new InlineKeyboardButton[3];
                                            }
                                            option = "";
                                        }
                                        if (i != 0)
                                        {
                                            while (i != 3)
                                            {
                                                bRow[i] = InlineKeyboardButton.WithCallbackData(text: $" ",
                                                  callbackData: $"doNothing");
                                                i++;
                                            }
                                            ikb.Add(bRow);
                                        }
                                    }
                                    else
                                    {
                                        List<string> selectedAuditoryIDs = cArgs[5].Split(sep, StringSplitOptions.RemoveEmptyEntries).ToList();

                                        response = response
                                            .Remove(response.IndexOf("Список всех аудиторий:") + 22, response.Length - response.IndexOf("Список всех аудиторий:") - 22)
                                            .Replace("Выбор аудитории:", "*Выбор аудитории:*")
                                            .Replace("Аудитория", "*Аудитория*") + "\n";
                                        if (auList.Count() == 0) response += "\nПо заданным фильтрам аудиторий не найдено.";
                                        i = 0;
                                        foreach (var au in auList)
                                        {
                                            response += getAuditoryInfoShorter(au) + "\n\n";
                                            if (selectedAuditoryIDs.Contains(au.ID.ToString())) option = "➖";
                                            else option = "➕";
                                            bRow[i] = InlineKeyboardButton.WithCallbackData(text: $"{option} {getAuditoryInfoShort(au)}",
                                                callbackData: $"Edit$Auditory${pfID}${cArgs[5]}${au.ID}${cArgs[3]}${cArgs[4]}");
                                            i++;
                                            if (i == 3)
                                            {
                                                ikb.Add(bRow);
                                                i = 0;
                                                bRow = new InlineKeyboardButton[3];
                                            }
                                        }
                                        if (i != 0)
                                        {
                                            while (i != 3)
                                            {
                                                bRow[i] = InlineKeyboardButton.WithCallbackData(text: $" ",
                                                  callbackData: $"doNothing");
                                                i++;
                                            }
                                            ikb.Add(bRow);
                                        }

                                        fifthArg = "$" + cArgs[5];

                                        response += "\n\n\n*Порядок выбора аудиторий определяет приоритет при назначении аудитории для занятия.* " +
                                                    "Первая выбранная аудитория имеет наибольший приоритет, " +
                                                    "вторая будет следующей по предпочтительности и т. д.\n" +
                                                    "*В первую очередь указывайте аудитории для практик и лабораторных, " +
                                                    "потом только аудитории для лекций*\n" +
                                                    "Если выбранная Вами аудитория не подходит для занятия по вместимости, " +
                                                    "то будет выбрана следующая по приоритетности подходящая аудитория.\n\n\n*Выбранные аудитории:*\n";

                                        //response += "\n\n\n*Выбранные аудитории:*\n";
                                        bool g = false;
                                        foreach (var au in allAu)
                                        {

                                            if (selectedAuditoryIDs.Contains(au.ID.ToString()))
                                            {
                                                if (g)
                                                {
                                                    response += ", ";
                                                }
                                                response += getAuditoryInfoShort(au);
                                                g = true;
                                            }                                          
                                        }
                                    }

                                    ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"Проектор: {getProjectorInfo(Convert.ToInt32(cArgs[3]))}",
                                                callbackData: $"Sort$Auditory${pfID}${getNextProjectorCode(Convert.ToInt32(cArgs[3]))}${cArgs[4]}{fifthArg}"),
                                                   InlineKeyboardButton.WithCallbackData(text: $"Корпус: {getDepartmentInfo(Convert.ToInt32(cArgs[4]))}",
                                                callbackData: $"Sort$Auditory${pfID}${cArgs[3]}${getNextDepartment(Convert.ToInt32(cArgs[4]))}{fifthArg}")});
                                    if (pfID != -1)
                                    {
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Auditory${pfID}"),
                                                   InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Auditory${pfID}$")});
                                    }
                                    else
                                    {
                                        ikb.Add(new[] {
                                        InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Auditory${pfID}"),
                                        InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Auditory${pfID}$"),
                                        InlineKeyboardButton.WithCallbackData(text: "Сохранить",
                                                callbackData: $"Choose$Auditory${pfID}{fifthArg}")});
                                    }
                                    break;
                            }
                            break;

                        case "Cancel":
                            clear = true;
                            if (cArgs[1] == "Auditory" && pfID == -1) updateInfo = true;
                            break;
                    }

                    if (clear)
                    {
                        response = callbackQuery.Message.Text.Remove(callbackQuery.Message.Text.IndexOf("Выбор"), callbackQuery.Message.Text.Length - callbackQuery.Message.Text.IndexOf("Выбор"))
                                    + "Что Вы хотите изменить?";
                    }
                    if (updateInfo)
                    {
                        if (pfID != -1)
                            response =
                                "*Вы редактируете информацию о следующей дисциплине:*\n"
                                + getPrefInfo(pref) +
                                "\n\nЧто Вы хотите изменить?";
                        else
                        {
                            if (tch == null) foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == callbackQuery.Message.Chat.Id) tch = t;
                            response =
                                $"Вы можете задать аудитории, дни недели и время занятий \"по умолчанию\", " +
                                $"эти значения будут применены для всех ваших дисциплин, " +
                                $"но Вы всегда сможете изменить их для каждой дисциплины по отдельности, используя /disciplines\n\n" +
                                getTeacherInfo(tch) +
                                $"\n\nЧто Вы хотите изменить?";
                        }
                    }
                    if (clear || updateInfo)
                    {
                        if (pfID != -1)
                            ikb.Add(new[]
                            {
                                InlineKeyboardButton.WithCallbackData(text: "Аудиторию", callbackData: $"Edit$Auditory${pfID}"),
                                InlineKeyboardButton.WithCallbackData(text: "Частоту", callbackData: $"Edit$Frequency${pfID}"),
                                InlineKeyboardButton.WithCallbackData(text: "Дни недели", callbackData: $"Edit$Weekdays${pfID}"),
                                InlineKeyboardButton.WithCallbackData(text: "Время", callbackData: $"Edit$Time${pfID}")
                        });
                        else
                            ikb.Add(new[]
                            {
                                InlineKeyboardButton.WithCallbackData(text: "Аудитории", callbackData: $"Edit$Auditory$-1"),
                                InlineKeyboardButton.WithCallbackData(text: "Дни недели", callbackData: $"Edit$Weekdays${pfID}"),
                                InlineKeyboardButton.WithCallbackData(text: "Время", callbackData: $"Edit$Time${pfID}")
                        });
                    }
                    InlineKeyboardMarkup rmp = new InlineKeyboardMarkup(ikb);

                    //if (splittedResponse.Count != 0)
                    //{
                    //    for (i = 0; i < splittedResponse.Count - 1; i++)
                    //    {
                    //        await botClient.SendTextMessageAsync(
                    //        chatId: callbackQuery.Message.Chat.Id,
                    //        text: splittedResponse[i],
                    //        parseMode: ParseMode.Markdown,
                    //        cancellationToken: cancellationToken);
                    //    }
                    //    await botClient.SendTextMessageAsync(
                    //        chatId: callbackQuery.Message.Chat.Id,
                    //        text: splittedResponse[splittedResponse.Count - 1],
                    //        parseMode: ParseMode.Markdown,
                    //        replyMarkup: rmp,
                    //        cancellationToken: cancellationToken);
                    //}

                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: response,
                        parseMode: ParseMode.Markdown,
                        replyMarkup: rmp);
                    await botClient.AnswerCallbackQueryAsync(callbackQueryId: callbackQuery.Id, text: noti);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            return;
        }

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;

        }
        public class NameAndRecords
        {
            public string name { get; set; }
            public Dictionary<string, DiscInfo> records { get; set; }
        }
        public class DiscInfo
        {
            public string group { get; set; }
            public int subgroup { get; set; }
            public int students { get; set; }
            public int type { get; set; }
            public int hours { get; set; }
            public Dictionary<string, object[]> teachers { get; set; }
        }
        public class ForName
        {
            public string name { get; set; }
        }
    }
}
