﻿using System;
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

            response += "Ваши пожелания:";
            response += "\nАудитория: ";
            if (aud != null) response +=
              "\n        Корпус: " + aud.Department + "\n" +
                "        Номер: " + aud.Number + "\n" +
                "        Вместимость: " + aud.Capacity + "\n" +
                "        Оборудование: " + aud.Equipment + "\n" +
                "        Проектор: " + aud.Projector + "\n" +
                "        Смарт-доска: " + aud.SmartDesc;
            else response += "Не задана";

            response += "\nЧастота: ";
            //if (preff.Weekdays != null) response += preff.Weekdays;
            //else response += "Не задана";

            response += "\nДни недели: ";
            if (preff.Weekdays != null) response += preff.Weekdays;
            else response += "Не заданы";

            response += "\nВремя: ";
            if (preff.TimeBegin != null) response += preff.TimeBegin + " - " + preff.TimeEnd;
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

                        foreach (var t in teachers) if (Convert.ToInt64(t.ChatID) == chatId) tch = t;
                        string response = "";

                        string[] cArgs = messageText.Split('Q');

                        if (tch != null)
                        {
                            Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");
                            switch (cArgs[0])
                            {
                                case "/d":
                                    break;
                                case "/dp":
                                    response += "Ваш список дисциплин:\n";
                                    foreach (Prefference pf in entities.Prefference.Where(x => x.TeacherID == tch.ID).ToList())
                                    {
                                        response += 
                                            getPreffInfo(pf) 
                                            + "\nИзменить пожелания: /pfQ" + pf.ID + "\n\n";
                                    }
                                    sentMessage = await botClient.SendTextMessageAsync(
                                        chatId: chatId,
                                        text: response,
                                        parseMode: ParseMode.Markdown,
                                        cancellationToken: cancellationToken);
                                    break;

                                case "/pf":
                                    string pfID = cArgs[1];
                                    bool success = false;
                                    try
                                    {
                                        preff = entities.Prefference.Single(x => x.ID.ToString() == pfID);
                                        if (tch.ID == preff.TeacherID)
                                        {
                                            response += 
                                                "*Вы редактируете информацию о следующей дисциплине:*\n" 
                                                + getPreffInfo(preff) 
                                                + "\n\nЧто вы хотите изменить?";
                                            success = true;
                                            sentMessage = await botClient.SendTextMessageAsync(
                                                chatId: chatId,
                                                text: response,
                                                parseMode: ParseMode.Markdown,
                                                replyMarkup: new InlineKeyboardMarkup(new[]
                                                {
                                                    InlineKeyboardButton.WithCallbackData(text: "Аудиторию", callbackData: $"Edit$Auditory${pfID}"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Частоту", callbackData: $"Edit$Frequency${pfID}"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Дни недели", callbackData: $"Edit$Weekdays${pfID}"),
                                                    InlineKeyboardButton.WithCallbackData(text: "Время", callbackData: $"Edit$Time${pfID}")
                                                }),
                                                cancellationToken: cancellationToken);
                                        }
                                        else
                                        {
                                            response += "Вы не имеете доступа к этой дисциплине.";
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        response += "Дисциплина не найдена.";
                                    }

                                    if (!success) sentMessage = await botClient.SendTextMessageAsync(
                                        chatId: chatId,
                                        text: response,
                                        parseMode: ParseMode.Markdown,
                                        cancellationToken: cancellationToken);
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
                                    sentMessage = await botClient.SendTextMessageAsync(
                                        chatId: chatId,
                                        text: "Вы успешно вышли из аккаунта.",
                                        parseMode: ParseMode.Markdown,
                                        cancellationToken: cancellationToken);
                                    break;

                                default:
                                    sentMessage = await botClient.SendTextMessageAsync(
                                        chatId: chatId,
                                        text: "You said:\n" + messageText,
                                        cancellationToken: cancellationToken);
                                    break;
                            }
                        }
                        else
                        {
                            if (messageText == "/start")
                            {
                                sentMessage = await botClient.SendTextMessageAsync(
                                    chatId: chatId,
                                    text: "Здравствуйте, перед началом работы необходимо авторизоваться, введите код авторизации.",
                                    cancellationToken: cancellationToken);
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
                                    sentMessage = await botClient.SendTextMessageAsync(
                                    chatId: chatId,
                                    text: $"Вы успешно авторизовались, {newTeacher.LastName} {newTeacher.FirstName} {newTeacher.MiddleName}.\nДля работы с ботом используйте меню, чтобы получить список команд используйте /help.",
                                    cancellationToken: cancellationToken);
                                }
                                else
                                {
                                    sentMessage = await botClient.SendTextMessageAsync(
                                    chatId: chatId,
                                    text: "Неверный код, попробуйте снова.",
                                    cancellationToken: cancellationToken);
                                }
                            }
                        }
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
                    //Teacher tch = null;
                    Discipline disc = null;
                    //Auditory aud = null;

                    switch (cArgs[0])
                    {
                        case "Edit":
                            switch (cArgs[1])
                            {
                                case "Auditory":
                                    response += callbackQuery.Message.Text.Replace("Что вы хотите изменить?", "*Выбор аудитории:*");
                                    foreach (var au in entities.Auditory.OrderBy(x => x.Department).ThenBy(x => x.Number).ToList())
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
                                                callbackData: $"Cancel$Auditory${pfID}")});
                                    break;

                                case "Frequency":
                                    i = 1;
                                    response += callbackQuery.Message.Text.Replace("Что вы хотите изменить?", "*Выбор частоты:*");
                                    preff = entities.Prefference.Single(x => x.ID == pfID);
                                    disc = entities.Discipline.Single(x => x.ID == preff.DisciplineID);
                                    response += $"\nУ этой дисциплины всего {disc.Hours} часов за семестр.\nВыберите один из вариантов:";

                                    //if (Convert.ToInt32(disc.Hours) / 16 == 1)
                                    //{
                                    //    response += $"\n{i}. Одна пара в неделю, каждую неделю, весь семестр";
                                    //    ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"{i}",
                                    //            callbackData: $"Choose$Frequency${pfID}$1")});
                                    //    i++;

                                    //    response += $"\n{i}. Одна пара в неделю, каждую неделю";
                                    //    ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"{i}",
                                    //            callbackData: $"Choose$Frequency${pfID}$2")});
                                    //    i++;
                                    //}
                                    //if (Convert.ToInt32(disc.Hours) >= 16)
                                    //{
                                    //    response += $"\n{i}. Одна пара в неделю, каждую неделю";
                                    //    ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: $"{i}",
                                    //            callbackData: $"Choose$Frequency${pfID}$3")});
                                    //    i++;
                                    //}
                                    ikb.Add(new[] {InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Frequency${pfID}")});
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
                                                callbackData: $"Cancel$Time${pfID}")});
                                    }
                                    else
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
                                        preff = entities.Prefference.Single(x => x.ID == pfID);
                                        if (preff.Weekdays != null) sw = preff.Weekdays.ToString().Split(sep, StringSplitOptions.RemoveEmptyEntries);
                                        foreach (var w in sw) selectedWeekdays[Array.IndexOf(weekdays, w)] = true;
                                    }
                                    else
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
                                        InlineKeyboardButton.WithCallbackData(text: "Сохранить",
                                                callbackData: $"Choose$Weekdays${pfID}${wd}"),
                                        InlineKeyboardButton.WithCallbackData(text: "Назад",
                                                callbackData: $"Cancel$Weekdays${pfID}")});
                                    break;
                            }
                            break;

                        case "Choose":
                            preff = entities.Prefference.Single(x => x.ID == pfID);
                            switch (cArgs[1])
                            {
                                case "Auditory":                                  
                                    preff.AuditoryID = Convert.ToInt32(cArgs[3]);                                 
                                    break;

                                case "Frequency":
                                    break;

                                case "Time":
                                    preff.TimeBegin = stime[Convert.ToInt32(cArgs[3])];
                                    preff.TimeEnd = stime[Convert.ToInt32(cArgs[4])];
                                    break;

                                case "Weekdays":                                  
                                    preff.Weekdays = cArgs[3];
                                    break;
                            }
                            entities.SaveChanges();
                            updateInfo = true;
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
                        response = 
                            "*Вы редактируете информацию о следующей дисциплине:*\n" 
                            + getPreffInfo(preff) +
                            "\n\nЧто вы хотите изменить?";
                    }
                    if (clear || updateInfo)
                    {
                        ikb.Add(new[]
                        {
                                InlineKeyboardButton.WithCallbackData(text: "Аудиторию", callbackData: $"Edit$Auditory${pfID}"),
                                InlineKeyboardButton.WithCallbackData(text: "Частоту", callbackData: $"Edit$Frequency${pfID}"),
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
                    await botClient.AnswerCallbackQueryAsync(callbackQueryId: callbackQuery.Id);

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