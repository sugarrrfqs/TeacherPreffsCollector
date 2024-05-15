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
using MongoDB.Driver;
using System.Configuration;

namespace TeacherPreffsCollector
{
    internal static class Program
    {
        public static TeacherPreffsEntities entities = new TeacherPreffsEntities();
        static List<Teacher> teachers = entities.Teacher.ToList();


        static async Task Main(string[] args)
        {
            var botClient = new TelegramBotClient(ConfigurationManager.ConnectionStrings["botKey"].ConnectionString);
            CancellationTokenSource cts = new();
            ReceiverOptions receiverOptions = new()
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            botClient.StartReceiving(   updateHandler: HandleUpdateAsync,
                                        pollingErrorHandler: HandlePollingErrorAsync,
                                        receiverOptions: receiverOptions,
                                        cancellationToken: cts.Token);

            var me = await botClient.GetMeAsync();

            Console.WriteLine($"Start listening for @{me.Username}");
            Console.ReadLine();

            // Send cancellation request to stop bot
            cts.Cancel();
        }

        static string getPreffInfo (Prefference preff)
        {
            string response= "";
            Discipline disc = null;
            Auditory aud = null;

            disc = entities.Discipline.Single(x => x.ID == preff.DisciplineID);
            aud = null;
            if (preff.AuditoryID != null) aud = entities.Auditory.Single(x => x.ID == preff.AuditoryID);

            response += "*" + disc.Name + "*" +
                "\nТип: " + disc.Type +
                "\nГруппа: " + disc.Group + " (" + disc.StudentsCount + " чел.)" +
                "\nОбщее кол-во часов в семестре: " + disc.Hours + "\n\n";

            response += "*Ваши пожелания:*";
            response += "\n*Аудитория:* ";
            if (aud != null) response +=
                "\n        Корпус: " + aud.Department +
                "\n        Номер: " + aud.Number +
                "\n        Вместимость: " + aud.Capacity +
                "\n        Оборудование: " + aud.Equipment +
                "\n        Проектор: " + aud.Projector +
                "\n        Смарт-доска: " + aud.SmartDesc;
            else response += "Не задана";

            response += "\n*Частота:* ";
            if (preff.BCFirstWeek != null) response += $"\nКол-во часов:" +
                    $"\n        До смены: " +
                    $"\n                По первым неделям - {preff.BCFirstWeek}" +
                    $"\n                По вторым неделям - {preff.BCSecondWeek}" +
                    $"\n        После смены: " +
                    $"\n                По первым неделям - {preff.ACFirstWeek}" +
                    $"\n                По вторым неделям - {preff.ACSecondWeek}";
            else response += "Не задана";

            response += "\n*Дни недели:* ";
            if (preff.Weekdays != null) response += preff.Weekdays;
            else response += "Не заданы";

            response += "\n*Время:* ";
            if (preff.TimeBegin != null) response += preff.TimeBegin + " - " + preff.TimeEnd;
            else response += "Не задано";

            return response;
        }
        static string getTeacherInfo(Teacher tch)
        {
            string response = "";

            response += "*Ваши пожелания:*";

            response += "\nДни недели: ";
            if (tch.Weekdays != null) response += tch.Weekdays;
            else response += "Не заданы";

            response += "\nВремя: ";
            if (tch.TimeBegin != null) response += tch.TimeBegin + " - " + tch.TimeEnd;
            else response += "Не задано";

            return response;
        }


        async static Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // Only process Message updates: https://core.telegram.org/bots/api#message
                if (update.Message is not null)
                {
                    Message sentMessage;
                    var message = update.Message;
                    // Only process text messages
                    if (message.Text is not null)
                    {
                        var messageText = message.Text;
                        var chatId = message.Chat.Id;

                        Prefference preff = null;
                        Teacher tch = null;
                        Discipline disc = null;
                        Auditory aud = null;
                        List<InlineKeyboardButton[]> ikb = new List<InlineKeyboardButton[]>();

                        foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == chatId) tch = t;
                        string response = "";

                        string[] cArgs = messageText.Split('Q');

                        if (tch != null)
                        {
                            Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");
                            switch (cArgs[0])
                            {
                                case "/dp":
                                    response += "Ваш список дисциплин:\n";
                                    foreach (Prefference pf in entities.Prefference.Where(x => x.TeacherID == tch.ID).ToList())
                                    {
                                        response += 
                                            getPreffInfo(pf) 
                                            + "\nИзменить пожелания: /pfQ" + pf.ID + "\n\n";
                                    }
                                    break;

                                case "/pf":
                                    if (cArgs.Length == 1)
                                    {
                                        response =
                                            $"Вы можете задать дни недели и время занятий \"по умолчанию\", " +
                                            $"эти значения будут применены для всех ваших дисциплин, " +
                                            $"но вы всегда сможете изменить их для каждой дисциплины по отдельности, используя /dp\n\n" +
                                            getTeacherInfo(tch) + 
                                            $"\n\nЧто вы хотите изменить?";
                                        ikb.Add(new[]
                                                {
                                                    InlineKeyboardButton.WithCallbackData(text: "Дни недели", callbackData: $"Edit$Weekdays$-1"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Время", callbackData: $"Edit$Time$-1")
                                                });
                                    }
                                    if (cArgs.Length == 2)
                                    {
                                        string pfID = cArgs[1];
                                        try
                                        {
                                            preff = entities.Prefference.Single(x => x.ID.ToString() == pfID);
                                            if (tch.ID == preff.TeacherID)
                                            {
                                                response =
                                                    "*Вы редактируете информацию о следующей дисциплине:*\n"
                                                    + getPreffInfo(preff)
                                                    + "\n\nЧто вы хотите изменить?";
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
                                        catch (Exception ex)
                                        {
                                            response = "Дисциплина не найдена.";
                                        }
                                    }
                                    break;

                                case "/help":
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
                                    response = $"Вы успешно авторизовались, {newTeacher.LastName} {newTeacher.FirstName} {newTeacher.MiddleName}.\nДля работы с ботом используйте меню, чтобы получить список команд используйте /help.";
                                }
                                else
                                {
                                    response = "Неверный код, попробуйте снова.";
                                }
                            }
                        }
                        InlineKeyboardMarkup rmp = new InlineKeyboardMarkup(ikb);
                        sentMessage = await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: response,
                            parseMode: ParseMode.Markdown,
                            replyMarkup: rmp,
                            cancellationToken: cancellationToken);
                    }
                }
                if (update.CallbackQuery is not null)
                {                
                    var callbackQuery = update.CallbackQuery;
                    var inlineMessageId = callbackQuery.InlineMessageId;
                    string[] cArgs = callbackQuery.Data.Split('$');
                    int pfID = Convert.ToInt32(cArgs[2]);
                    string response = "";
                    int i = 0;
                    bool clear = false;
                    bool updateInfo = false;
                    string noti = "";

                    Console.WriteLine($"Pressed inline button, Data = {callbackQuery.Data}");

                    string[] stime = { "8:00", "9:40", "11:30", "13:20", "15:00", "16:40", "18:20" };
                    string[] etime = {  "8:00 (Конец занятия: 9:30)",
                                                        "9:40 (Конец занятия: 9:30)",
                                                        "11:30 (Конец занятия: 13:00)",
                                                        "13:20 (Конец занятия: 14:50)",
                                                        "15:00 (Конец занятия: 16:30)",
                                                        "16:40 (Конец занятия: 18:10)",
                                                        "18:20 (Конец занятия: 19:50)"};
                    string[] weekdays = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
                    string[] weekdaysFull = { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };
                    bool[] selectedWeekdays = { false, false, false, false, false, false };

                    InlineKeyboardButton[] buttonRow = new InlineKeyboardButton[2];
                    List<InlineKeyboardButton[]> ikb = new List<InlineKeyboardButton[]>();
                    Prefference preff = null;
                    Teacher tch = null;
                    Discipline disc = null;
                    //Auditory aud = null;

                    switch (cArgs[0])
                    {
                        case "Edit":
                            switch (cArgs[1])
                            {
                                case "Auditory":
                                    response += callbackQuery.Message.Text.Replace("Что вы хотите изменить?", "*Выбор аудитории:*");
                                    preff = entities.Prefference.Single(x => x.ID == pfID);
                                    disc = entities.Discipline.Single(x => x.ID == preff.DisciplineID);
                                    foreach (var au in entities.Auditory.Where(x => x.Capacity >= disc.StudentsCount).OrderBy(x => x.Department).ThenBy(x => x.Number).ToList())
                                    {
                                        response += "\n*Аудитория:* \n" +
                                            "Корпус: " + au.Department + "\n" +
                                            "Номер: " + au.Number + "\n" +
                                            "Вместимость: " + au.Capacity + "\n" +
                                            "Кол-во рабочих мест (компьютеров): " + au.Workplaces + "\n" +
                                            "Оборудование: " + au.Equipment + "\n" +
                                            "Проектор: " + au.Projector + "\n" +
                                            "Смарт-доска: " + au.SmartDesc + "\n";
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Корпус: " + au.Department + ", Номер: " + au.Number,
                                                callbackData: $"Choose$Auditory${pfID}${au.ID}")});
                                    }
                                    ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Auditory${pfID}"),
                                                   InlineKeyboardButton.WithCallbackData(text: "Сбросить",
                                                callbackData: $"Choose$Auditory${pfID}$")});                               
                                    break;

                                case "Frequency":
                                    preff = entities.Prefference.Single(x => x.ID == pfID);
                                    disc = entities.Discipline.Single(x => x.ID == preff.DisciplineID);
                                    int hours = Convert.ToInt32(disc.Hours);
                                    int hBefore = 0, hAfter = 0, hPerWeek = 0, hPerFWeek = 0, hPerSWeek = 0;

                                    response += callbackQuery.Message.Text
                                        .Replace("Выбор частоты:", "*Выбор частоты:*")
                                        .Replace("Что вы хотите изменить?", $"*Выбор частоты:*\nУ этой дисциплины всего {hours} часов за семестр.")
                                        .Replace("Выберите распределение на первую половину семестра:", "")
                                        .Replace("Выберите распределение на вторую половину семестра:", "");

                                    if (response.IndexOf("часов за семестр.") > 0) response = response.Remove(response.IndexOf("часов за семестр.") + 17, response.Length - response.IndexOf("часов за семестр.") - 17);                                    

                                    if (cArgs.Length == 3)
                                    {
                                        hAfter = hAfter = (hours - hours % 16) / 2;
                                        if (hours % 16 < 8) hBefore = hAfter;
                                        else hBefore = hours - hAfter;

                                        if (disc.Type != "Лек") (hBefore, hAfter) = (hAfter, hBefore); //swap часов для практик и лаб

                                        response += $"\n\n*Выберите распределение на семестр:*";

                                        response += $"\n1. До и после смены расписания (Кол-во часов до смены: {hBefore}, Кол-во часов после смены: {hAfter})" +
                                                    $"\n\n2. Только до смены расписания" +
                                                    $"\n\n3. Только после смены расписания";

                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Edit$Frequency${pfID}$1")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Edit$Frequency${pfID}$2")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"3",
                                                callbackData: $"Edit$Frequency${pfID}$3")});
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
                                                hAfter = hAfter = (hours - hours % 16) / 2;
                                                if (hours % 16 < 8) hBefore = hAfter;
                                                else hBefore = hours - hAfter;
                                                hPerWeek = hBefore / 8;
                                                if (hPerWeek % 2 != 0)
                                                {
                                                    hPerFWeek = hPerWeek + 1;
                                                    hPerSWeek = hPerWeek - 1;
                                                }
                                                else
                                                {
                                                    hPerFWeek = hPerWeek;
                                                    hPerSWeek = hPerWeek;
                                                }

                                                response += "\n\n*Выберите распределение на первую половину семестра:*" +
                                                    $"\n1. Равномерно (Кол-во часов: по первым неделям - {hPerFWeek}, по вторым неделям - {hPerSWeek})" +
                                                    $"\n\n2. Только по первым неделям (Кол-во часов по первым неделям - {hPerWeek * 2})" +
                                                    $"\n\n3. Только по вторым неделям (Кол-во часов по вторым неделям - {hPerWeek * 2})";
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Edit$Frequency${pfID}${cArgs[3]}${hPerFWeek}-{hPerSWeek}")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Edit$Frequency${pfID}${cArgs[3]}${hPerWeek * 2}-0")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"3",
                                                callbackData: $"Edit$Frequency${pfID}${cArgs[3]}$0-{hPerWeek * 2}")});
                                                break;

                                            case "2":
                                                hPerWeek = hours / 8;
                                                if (hPerWeek % 2 != 0)
                                                {
                                                    hPerFWeek = hPerWeek + 1;
                                                    hPerSWeek = hPerWeek - 1;
                                                }
                                                else
                                                {
                                                    hPerFWeek = hPerWeek;
                                                    hPerSWeek = hPerWeek;
                                                }
                                                response += "\n\n*Выберите распределение на первую половину семестра:*" +
                                                    $"\n1. Равномерно (Кол-во часов: по первым неделям - {hPerFWeek}, по вторым неделям - {hPerSWeek})" +
                                                    $"\n\n2. Только по первым неделям (Кол-во часов по первым неделям - {hPerWeek * 2})" +
                                                    $"\n\n3. Только по вторым неделям (Кол-во часов по вторым неделям - {hPerWeek * 2})";
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Choose$Frequency${pfID}${hPerFWeek}-{hPerSWeek}-0-0")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Choose$Frequency${pfID}${hPerWeek * 2}-0-0-0")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"3",
                                                callbackData: $"Choose$Frequency${pfID}$0-{hPerWeek * 2}-0-0")});

                                                break;

                                            case "3":
                                                hPerWeek = hours / 8;
                                                if (hPerWeek % 2 != 0)
                                                {
                                                    hPerFWeek = hPerWeek + 1;
                                                    hPerSWeek = hPerWeek - 1;
                                                }
                                                else
                                                {
                                                    hPerFWeek = hPerWeek;
                                                    hPerSWeek = hPerWeek;
                                                }
                                                response += "\n\n*Выберите распределение на вторую половину семестра:*" +
                                                    $"\n1. Равномерно (Кол-во часов: по первым неделям - {hPerFWeek}, по вторым неделям - {hPerSWeek})" +
                                                    $"\n\n2. Только по первым неделям (Кол-во часов по первым неделям - {hPerWeek * 2})" +
                                                    $"\n\n3. Только по вторым неделям (Кол-во часов по вторым неделям - {hPerWeek * 2})";
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Choose$Frequency${pfID}$0-0-{hPerFWeek}-{hPerSWeek}")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Choose$Frequency${pfID}$0-0-{hPerWeek * 2}-0")});
                                                ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"3",
                                                callbackData: $"Choose$Frequency${pfID}$0-0-0-{hPerWeek * 2}")});

                                                break;
                                        }
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Edit$Frequency${pfID}")});
                                    }

                                    if (cArgs.Length == 5)
                                    {
                                        hAfter = hAfter = (hours - hours % 16) / 2;
                                        hPerWeek = hAfter / 8;
                                        if (hPerWeek % 2 != 0)
                                        {
                                            hPerFWeek = hPerWeek + 1;
                                            hPerSWeek = hPerWeek - 1;
                                        }
                                        else
                                        {
                                            hPerFWeek = hPerWeek;
                                            hPerSWeek = hPerWeek;
                                        }

                                        response += "\n\n*Выберите распределение на вторую половину семестра:*" +
                                            $"\n1. Равномерно (Кол-во часов: по первым неделям - {hPerFWeek}, по вторым неделям - {hPerSWeek})" +
                                            $"\n\n2. Только по первым неделям (Кол-во часов по первым неделям - {hPerWeek * 2})" +
                                            $"\n\n3. Только по вторым неделям (Кол-во часов по вторым неделям - {hPerWeek * 2})";

                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"1",
                                                callbackData: $"Choose$Frequency${pfID}${cArgs[4]}-{hPerFWeek}-{hPerSWeek}")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"2",
                                                callbackData: $"Choose$Frequency${pfID}${cArgs[4]}-{hPerWeek * 2}-0")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"3",
                                                callbackData: $"Choose$Frequency${pfID}${cArgs[4]}-0-{hPerWeek * 2}")});
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Edit$Frequency${pfID}$1")});
                                    }
                                    break;

                                case "Time":                                  
                                    if (cArgs.Length == 3)
                                    {
                                        response += callbackQuery.Message.Text
                                            .Replace("Выбор времени:", "*Выбор времени:*")
                                            .Replace("Что вы хотите изменить?", "*Выбор времени:\n*")
                                            .Replace("Выберите конец удобного вам промежутка времени занятий:", "");
                                        if (response.IndexOf("Выбранное") >= 0) response = response.Remove(response.IndexOf("Выбранное"), response.Length - response.IndexOf("Выбранное"));
                                        response += "Выберите *начало* удобного вам промежутка времени занятий:";
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
                                            .Replace("Выбор времени:", $"*Выбор времени:*\nВыбранное вами начало: {stime[beg]}");
                                        
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
                                            .Replace("Что вы хотите изменить?", "*Выбор дней недели:\n*Выбранные дни:\n");
                                    char[] sep = new char[] { ' ', ',' };
                                    string[] sw = new string[0];

                                    if (cArgs.Length == 3)
                                    {
                                        if (pfID != -1)
                                        {
                                            preff = entities.Prefference.Single(x => x.ID == pfID);
                                            if (preff.Weekdays != null) sw = preff.Weekdays.ToString().Split(sep, StringSplitOptions.RemoveEmptyEntries);
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
                                            .Replace("Выбор дней недели:", "*Выбор дней недели:*") +'\n';

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
                                        ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: weekdays[i],
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
                                preff = entities.Prefference.Single(x => x.ID == pfID);
                                switch (cArgs[1])
                                {
                                    case "Auditory":
                                        if (cArgs[3] != "") preff.AuditoryID = Convert.ToInt32(cArgs[3]);
                                        else preff.AuditoryID = null;
                                        break;

                                    case "Frequency":
                                        if (cArgs[3] != "")
                                        {
                                            string[] h = cArgs[3].Split('-');
                                            preff.BCFirstWeek = Convert.ToInt16(h[0]);
                                            preff.BCSecondWeek = Convert.ToInt16(h[1]);
                                            preff.ACFirstWeek = Convert.ToInt16(h[2]);
                                            preff.ACSecondWeek = Convert.ToInt16(h[3]);
                                        }
                                        else
                                        {
                                            preff.BCFirstWeek = null;
                                            preff.BCSecondWeek = null;
                                            preff.ACFirstWeek = null;
                                            preff.ACSecondWeek = null;
                                        }
                                        break;

                                    case "Time":
                                        if (cArgs[3] != "" && cArgs[4] != "")
                                        {
                                            preff.TimeBegin = stime[Convert.ToInt32(cArgs[3])];
                                            preff.TimeEnd = stime[Convert.ToInt32(cArgs[4])];
                                        }
                                        else
                                        {
                                            preff.TimeBegin = null;
                                            preff.TimeEnd = null;
                                        }
                                        break;

                                    case "Weekdays":
                                        if (cArgs[3] != "") preff.Weekdays = cArgs[3];
                                        else preff.Weekdays = null;
                                        break;
                                }
                                updateInfo = true;
                            }
                            else
                            {
                                foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == callbackQuery.Message.Chat.Id) tch = t;
                                switch (cArgs[1])
                                {
                                    case "Time":
                                        if (cArgs[3] != "" && cArgs[4] != "")
                                        {
                                            tch.TimeBegin = stime[Convert.ToInt32(cArgs[3])];
                                            tch.TimeEnd = stime[Convert.ToInt32(cArgs[4])];
                                            foreach (var p in entities.Prefference.ToList())
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
                                            foreach (var p in entities.Prefference.ToList())
                                            {
                                                p.Weekdays = cArgs[3];
                                            }
                                        }
                                        else tch.Weekdays = null;
                                        break;
                                }
                                updateInfo = true;
                            }
                            entities.SaveChanges();
                            noti = "Данные успешно сохранены.";
                            break;

                        case "Cancel":
                            clear = true;
                            break;

                        default:
                            break;
                    }


                    if (clear)
                    {
                        response = callbackQuery.Message.Text.Remove(callbackQuery.Message.Text.IndexOf("Выбор"), callbackQuery.Message.Text.Length - callbackQuery.Message.Text.IndexOf("Выбор"))
                                    + "Что вы хотите изменить?";                      
                    }
                    if (updateInfo)
                    {
                        if (pfID != -1)
                            response = 
                                "*Вы редактируете информацию о следующей дисциплине:*\n" 
                                + getPreffInfo(preff) +
                                "\n\nЧто вы хотите изменить?";
                        else
                            response =
                                $"Вы можете задать дни недели и время занятий \"по умолчанию\", " +
                                $"эти значения будут применены для всех ваших дисциплин, " +
                                $"но вы всегда сможете изменить их для каждой дисциплины по отдельности, используя /dp\n\n" +
                                getTeacherInfo(tch) +
                                $"\n\nЧто вы хотите изменить?";
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
                                InlineKeyboardButton.WithCallbackData(text: "Дни недели", callbackData: $"Edit$Weekdays${pfID}"),
                                InlineKeyboardButton.WithCallbackData(text: "Время", callbackData: $"Edit$Time${pfID}")
                        });
                    }
                    InlineKeyboardMarkup rmp = new InlineKeyboardMarkup(ikb);
                    await botClient.EditMessageTextAsync(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        text: response,
                        parseMode: ParseMode.Markdown,
                        replyMarkup: rmp);
                    await botClient.AnswerCallbackQueryAsync(callbackQueryId: callbackQuery.Id, text: noti);

                }
            }
            catch(Exception ex)
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
    }
}
