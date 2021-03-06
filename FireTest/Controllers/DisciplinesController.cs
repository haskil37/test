﻿using FireTest.Models;
using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace FireTest.Controllers
{
    [Authorize]
    public class DisciplinesController : Controller
    {
        ApplicationDbContext dbContext = new ApplicationDbContext();
        private static Random random = new Random();
        public ActionResult Index(int id = 0)
        {
            if (!ModelState.IsValid || id < 1 || dbContext.Subjects.Find(id) == null)
                return RedirectToAction("Index", "Home");

            ViewBag.DisciplineName = dbContext.Subjects.Find(id).Name;

            string user = User.Identity.GetUserId();

            var end = dbContext.SelfyTestDisciplines //Есть ли незаконченный или пустой тест
                .Where(u => u.IdUser == user)
                .Where(u => u.End == false)
                .Select(u => new {
                    id = u.Id,
                    questions = u.Questions,
                    idSubject = u.IdSubject,
                }).FirstOrDefault();
            ViewBag.Сompleted = true;

            if (end != null)
            {
                if (!string.IsNullOrEmpty(end.questions)) //Если незаконченный тест, то предлагаем закончить
                {
                    var name = dbContext.Subjects.Find(end.idSubject);
                    if (name != null)
                    {
                        ViewBag.DisciplineName = name.Name;
                        ViewBag.Id = end.id;
                        ViewBag.EndId = id;
                        ViewBag.Сompleted = false;
                    }
                    else
                    {   
                        //Если такой дисциплины больше нет, но тест не закончен - обнуляем
                        SelfyTestDiscipline continueTest = dbContext.SelfyTestDisciplines.Find(end.id);
                        continueTest.IdSubject = id;
                        continueTest.Questions = null;
                        continueTest.Answers = null;
                        continueTest.RightOrWrong = null;
                        dbContext.SaveChanges();
                        ViewBag.Id = end.id;
                    }
                }
                else  //Если пустой тест, то меняем значение предмета
                {
                    SelfyTestDiscipline continueTest = dbContext.SelfyTestDisciplines.Find(end.id);
                    continueTest.IdSubject = id;
                    dbContext.SaveChanges();
                    ViewBag.Id = end.id;
                }
            }
            else //Если нет, то создаем пустой
            {
                SelfyTestDiscipline newTest = new SelfyTestDiscipline
                {
                    IdUser = user,
                    IdSubject = id,
                    TimeStart = DateTime.Now,
                    TimeEnd = DateTime.Now
                };
                dbContext.SelfyTestDisciplines.Add(newTest);
                dbContext.SaveChanges();
                ViewBag.Id = newTest.Id;
            }
            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public PartialViewResult IndexCount(int idtest, int course = 1)
        {
            try
            {
                if (!ModelState.IsValid || course < 1 || course > 5)
                    return PartialView();

                ViewBag.Course = course;

                var idSubject = dbContext.SelfyTestDisciplines //Берем id предмета
                        .SingleOrDefault(u => u.Id == idtest).IdSubject;

                int count = dbContext.Questions
                            .Where(u => u.IdSubject == idSubject)
                            .Where(u => u.IdCourse == course).Count();
                ViewBag.Count = count;
                ViewBag.CountMax = Math.Ceiling(count / 10.0);
            }
            catch (Exception ex)
            {
                ViewBag.Message = ex;
                return PartialView("Error");
            }
            return PartialView();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(int? EndId, int id = 0, string submitButton = "Exit", int count = 10, int course = 1)
        {
            if (!ModelState.IsValid || id == 0 || submitButton == "Exit")
                return RedirectToAction("Index", "Home");

            string user = User.Identity.GetUserId();

            var test = dbContext.SelfyTestDisciplines //Проверяем правильность id
                            .Where(u => u.IdUser == user)
                            .Where(u => u.Id == id)
                            .Select(u => new {
                                questions = u.Questions,
                                idSubject = u.IdSubject,
                            }).SingleOrDefault();

            if (test == null) //Если id туфта, то выходим
                return RedirectToAction("Index", "Home");

            if (submitButton == "Cancel") //Удаляем пустой или незаконченный тесты при отмене
            {
                var deleteTest = dbContext.SelfyTestDisciplines.Find(id);
                dbContext.SelfyTestDisciplines.Remove(deleteTest);
                dbContext.SaveChanges();
                return RedirectToAction("Index", new { id = EndId });
            }

            if (submitButton != "Accept") //Т.к. Cancel мы обработали, то если будет любой текст кроме согласия, то это фигня и мы выкидываем пользователя на главную
                return RedirectToAction("Index", "Home");

            if (string.IsNullOrEmpty(test.questions)) //Создаем новый тест
            {
                count = count * 10;
                if (!AutoSelectQuestion(id, test.idSubject, course, count))
                    return RedirectToAction("Index", "Home"); //Если что-то пошло не так, то на главную
            }
            else //Продолжаем старый тест
            {
                //Ничего не делаем :)
            }

            ApplicationUser userBusy = dbContext.Users.Find(User.Identity.GetUserId()); //Начали тест - делаем юзера занятым, чтобы нельзя было вызвать на бой
            userBusy.Busy = true;
            dbContext.SaveChanges();

            return RedirectToAction("DisciplineTest");
        }
        public ActionResult DisciplineTest()
        {
            string user = User.Identity.GetUserId();
            var disciplineTest = dbContext.SelfyTestDisciplines
                .Where(u => u.IdUser == user)
                .Where(u => string.IsNullOrEmpty(u.Questions) != true)
                .Where(u => u.End == false)
                .Select(u => new {
                    id = u.Id,
                    idSubject = u.IdSubject,
                    questions = u.Questions,
                    answers = u.Answers
                }).SingleOrDefault();
            if (disciplineTest == null) //Если нет теста, то на главную
                return RedirectToAction("Index", "Home");

            int count = disciplineTest.questions.Split('|').ToList().Count(); //Общее количество вопросов
            int number = 0;
            if (!string.IsNullOrEmpty(disciplineTest.answers))
                number = disciplineTest.answers.Split('|').ToList().Count(); //Общее количество ответов

            if (count == number) //Если ответов столько же сколько и вопросов то идем на страницу статистики.
                return RedirectToAction("DisciplineTestEnd");

            ViewBag.DisciplineName = dbContext.Subjects.Find(disciplineTest.idSubject).Name;
            Questions model = new Questions();
            model = SelectQuestion(disciplineTest.id);
            if (model == null)
                return RedirectToAction("DisciplineTestEnd");

            ViewBag.Count = disciplineTest.questions.Split('|').ToList().Count();
            if (!string.IsNullOrEmpty(disciplineTest.answers))
                ViewBag.Number = disciplineTest.answers.Split('|').ToList().Count() + 1;
            else
            {
                SelfyTestDiscipline test = dbContext.SelfyTestDisciplines.Find(disciplineTest.id); //Обновляем время начала теста, т.к. это первый запуск
                test.TimeStart = DateTime.Now;
                test.TimeEnd = DateTime.Now;
                dbContext.SaveChanges();
                ViewBag.Number = 1;
            }
            return View(model);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public PartialViewResult DisciplineTestQuestions(List<string> AnswersIDs)
        {
            string user = User.Identity.GetUserId();
            if (dbContext.Users.Find(user).LastActivity < DateTime.Now.AddSeconds(-30)) //Проверка на оффлайн для ботов
                return PartialView();

            var disciplineTest = dbContext.SelfyTestDisciplines //Берем id теста
                .Where(u => u.IdUser == user)
                .Where(u => string.IsNullOrEmpty(u.Questions) != true)
                .Where(u => u.End == false)
                .Select(u => new {
                    id = u.Id,
                    idSubject = u.IdSubject,
                    questions = u.Questions,
                    answers = u.Answers
                }).SingleOrDefault();

            List<int> AnswersIDsINT = new List<int>();
            if (AnswersIDs != null)
            {
                foreach (var item in AnswersIDs)
                    AnswersIDsINT.Add(CodeDecode(item));
            }
            else
                AnswersIDsINT.Add(0);

            if (!SaveAnswer(disciplineTest.id, AnswersIDsINT))
                return PartialView();

            int count = disciplineTest.questions.Split('|').ToList().Count(); //Общее количество вопросов
            int number = 0;
            if (!string.IsNullOrEmpty(disciplineTest.answers))
                number = disciplineTest.answers.Split('|').ToList().Count() + 1; //Общее количество ответов +1, т.к. запрос был раньше чем добавлен в базу новый ответ.
            else
                number = 1; //Общее количество ответов 1, т.к. запрос был раньше чем добавлен в базу новый ответ.

            if (count == number) //Если ответов столько же сколько и вопросов то идем на страницу статистики.
            {
                ViewBag.DisciplineTestEnd = true;
                return PartialView();
            }
            else //Иначе продолжаем тестирование
            {
                ViewBag.Count = count;
                ViewBag.Number = number + 1;
                ViewBag.DisciplineName = dbContext.Subjects.Find(disciplineTest.idSubject).Name;
                ViewBag.DisciplineTestEnd = false;

                Questions model = new Questions();
                model = SelectQuestion(disciplineTest.id);
                if (model == null)
                {
                    ViewBag.DisciplineTestEnd = true;
                    return PartialView();
                }
                return PartialView(model);
            }
        }
        public ActionResult DisciplineTestEnd()
        {
            string user = User.Identity.GetUserId();
            var disciplineTest = dbContext.SelfyTestDisciplines
                .Where(u => u.IdUser == user)
                .Where(u => string.IsNullOrEmpty(u.Questions) != true)
                .Where(u => u.End == false)
                .Select(u => new {
                    id = u.Id,
                    questions = u.Questions,
                    answers = u.RightOrWrong,
                }).SingleOrDefault();
            if (disciplineTest == null) //Если нет теста, то на главную
                return RedirectToAction("Index", "Home");

            int questions = disciplineTest.questions.Split('|').Count();
            int answers = disciplineTest.answers.Split('|').Count();
            if (questions != answers) //Если тест еще не закончен то шлем на тест
                return RedirectToAction("DisciplineTest");

            SelfyTestDiscipline test = dbContext.SelfyTestDisciplines.Find(disciplineTest.id); //Заканчиваем тест
            test.End = true;
            test.TimeEnd = DateTime.Now;
            ViewBag.Days = (test.TimeEnd - test.TimeStart).Days;
            ViewBag.Hours = (test.TimeEnd - test.TimeStart).Hours;
            ViewBag.Min = (test.TimeEnd - test.TimeStart).Minutes;
            ViewBag.Sec = (test.TimeEnd - test.TimeStart).Seconds;
            ViewBag.DisciplineName = dbContext.Subjects.Find(test.IdSubject).Name;
            dbContext.SaveChanges();

            List<string> right = new List<string>(); //Берем правильные и неправильные ответы
            int countId = 0;
            foreach (string item in test.RightOrWrong.Split('|').ToList())
            {
                if (item != "0")
                    right.Add(item);
                countId++;
            }
            int count = test.RightOrWrong.Split('|').ToList().Count();
            ViewBag.Count = count;

            ViewBag.RightP = right.Count() * 100 / count;
            ViewBag.Right = right.Count();

            ApplicationUser userBusy = dbContext.Users.Find(User.Identity.GetUserId()); //Закончили тест - делаем юзера свободным
            userBusy.Busy = false;
            userBusy.Rating += (right.Count() / 10.0) + (right.Count() * 100 / count) / 10.0;
            dbContext.SaveChanges();
            ViewBag.Avatar = "/Images/Avatars/" + userBusy.Avatar;
            ViewBag.Details = disciplineTest.id;

            return View();
        }
        public ActionResult DisciplineWrongDetails(int id)
        {
            string user = User.Identity.GetUserId();
            var disciplineTest = dbContext.SelfyTestDisciplines
                .Where(u => u.IdUser == user)
                .Where(u => u.Id == id)
                .Where(u => u.End == true)
                .Select(u => new {
                    rightOrWrong = u.RightOrWrong,
                    questions = u.Questions,
                    answers = u.Answers
                }).SingleOrDefault();
            if (disciplineTest == null) //Если нет теста, то на главную
                return RedirectToAction("Index", "Home");
            int questions = disciplineTest.questions.Split('|').Count();
            int answers = disciplineTest.answers.Split('|').Count();

            //Берем неправильные ответы и из каких они дисциплин
            List<string> wrong = new List<string>();
            List<string> idQuestions = disciplineTest.questions.Split('|').ToList();
            int countId = 0;
            foreach (string item in disciplineTest.rightOrWrong.Split('|').ToList())
            {
                if (item == "0")
                    wrong.Add(idQuestions[countId]);
                countId++;
            }
            List<TestWrongAnswersDetails> AllTestWrongAnswersDetails = new List<TestWrongAnswersDetails>();
            if (wrong.Count() > 0)
            {
                foreach (string item in wrong)
                {
                    int temp = Convert.ToInt32(item);
                    var wrongDetails = new TestWrongAnswersDetails
                    {
                        Question = dbContext.Questions.Find(temp).QuestionText
                    };
                    List<string> idCorrect = dbContext.Questions.Find(temp).IdCorrect.Split(',').ToList();

                    if (idCorrect[0][0] == '~') //Если вопрос на последовательность
                    {
                        wrongDetails.TypeQuestion = "sequence";
                        var allAnswers = dbContext.Answers.Where(u => u.IdQuestion == temp).Select(u => new {
                            answerText = u.AnswerText,
                            answerId = u.Id
                        }).ToList();
                        idCorrect[0] = idCorrect[0].Remove(0, 1);

                        var correctAnswers = new List<string>();
                        for (int i = 0; i < idCorrect.Count(); i++)
                        {
                            //var y = allAnswers.Find(u => u.answerId.ToString() == idCorrect[i]).answerText;
                            var text = allAnswers.Where(u => u.answerId.ToString() == idCorrect[i]).Select(u => u.answerText).SingleOrDefault();
                            correctAnswers.Add(text);
                        }
                        wrongDetails.CorrectAnswers = correctAnswers;
                        AllTestWrongAnswersDetails.Add(wrongDetails);
                    }
                    else if (idCorrect[0][0] == '#') //Если вопрос на соответствие
                    {
                        wrongDetails.TypeQuestion = "conformity";
                        var allAnswers = dbContext.Answers.Where(u => u.IdQuestion == temp).Select(u => new {
                            answerText = u.AnswerText,
                            answerId = u.Id
                        }).ToList();
                        idCorrect[0] = idCorrect[0].Remove(0, 1);

                        var correctAnswers = new List<string>();
                        var wrongAnswers = new List<string>();
                        for (int i = 0; i < idCorrect.Count; i++)
                        {
                            var leftRight = idCorrect[i].Split('=');
                            correctAnswers.Add(allAnswers.Where(u => u.answerId.ToString() == leftRight[0]).Select(u => u.answerText).SingleOrDefault());
                            wrongAnswers.Add(allAnswers.Where(u => u.answerId.ToString() == leftRight[1]).Select(u => u.answerText).SingleOrDefault());
                        }
                        wrongDetails.WrongAnswers = wrongAnswers;
                        wrongDetails.CorrectAnswers = correctAnswers;
                        AllTestWrongAnswersDetails.Add(wrongDetails);
                    }
                    else
                    {
                        wrongDetails.TypeQuestion = "standart";
                        var allAnswers = dbContext.Answers.Where(u => u.IdQuestion == temp).Select(u => new {
                            answerText = u.AnswerText,
                            answerId = u.Id
                        }).ToList();

                        var correctAnswers = new List<string>();
                        var wrongAnswers = new List<string>();
                        foreach (var answersItem in allAnswers)
                        {
                            if (idCorrect.Contains(answersItem.answerId.ToString()))
                                correctAnswers.Add(answersItem.answerText);
                            else
                                wrongAnswers.Add(answersItem.answerText);
                        }
                        wrongDetails.WrongAnswers = wrongAnswers;
                        wrongDetails.CorrectAnswers = correctAnswers;
                        AllTestWrongAnswersDetails.Add(wrongDetails);
                    }
                }
            }
            return View(AllTestWrongAnswersDetails);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Issue(string QID, string Issue)
        {
            int que = CodeDecode(QID);
            string Result = "Произошел сбой. Сообщение не доставлено";
            if (que != 0)
            {
                if (string.IsNullOrEmpty(Issue.Trim()))
                    Result = "Вы должны описать проблему";
                else
                {
                    var result = dbContext.Questions.Find(que);
                    if (result != null)
                    {
                        dbContext.Issues.Add(new Issue
                        {
                            QuestionId = que,
                            SubjectId = result.IdSubject,
                            Message = Issue,
                            UserId = User.Identity.GetUserId()
                        });
                        dbContext.SaveChanges();
                        Result = "Сообщение доставлено";
                    }
                }
            }
            
            return Content(Result);
        }
        #region Вспомогательные приложения
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dbContext.Dispose();
            }
            base.Dispose(disposing);
        }
        private bool AutoSelectQuestion(int id, int idSubject, int course, int count)
        {
            try
            {
                List<int> idQuestions = new List<int>();
                var questionsId = dbContext.Questions
                     .Where(u => u.IdSubject == idSubject)
                     .Where(u => u.IdCourse == course)
                     .Select(u => new
                     {
                         id = u.Id
                     }).ToList();

                if (count >= questionsId.Count) //Если выбрали вопросов больше или столько же сколько в базе, то делаем максимум что есть
                {
                    for (int i = 0; i < questionsId.Count; i++)
                        idQuestions.Add(questionsId.ElementAt(i).id);
                    Shuffle(idQuestions); //Берем все вопросы без рандома и перемешиваем.
                }
                else //Иначе набираем рандомно вопросы
                {
                    Random rnd = new Random();
                    while (idQuestions.Count != count)
                    {
                        int value = rnd.Next(questionsId.Count());
                        int item = questionsId.ElementAt(value).id;

                        if (!idQuestions.Contains(item))
                            idQuestions.Add(item);
                    }
                }

                string questions = ""; //Сохраняем вопросы в тест
                foreach (int item in idQuestions)
                {
                    questions += item.ToString() + "|";
                }
                questions = questions.Substring(0, questions.Length - 1);
                var test = dbContext.SelfyTestDisciplines.Find(id);
                test.Questions = questions;
                dbContext.SaveChanges();
            }
            catch
            {
                return false;
            }
            return true;
        }
        static void Shuffle<T>(List<T> array)
        {
            int n = array.Count();
            for (int i = 0; i < n; i++)
            {
                int r = i + (int)(random.NextDouble() * (n - i));
                T t = array[r];
                array[r] = array[i];
                array[i] = t;
            }
        }
        private Questions SelectQuestion(int id)
        {
            Questions question = new Questions();
            Question questionDB;
            int CurrentQuestion = CountAnswers(id);
            do
            {
                questionDB = dbContext.Questions.Find(CurrentQuestion);
                if (questionDB == null) //Если вопрос удален, делаем ответ верным и идем дальше
                {
                    SelfyTestDiscipline test = dbContext.SelfyTestDisciplines.Find(id);
                    if (!string.IsNullOrEmpty(test.Answers))
                        test.Answers += "|0";
                    else
                        test.Answers = "0";

                    if (!string.IsNullOrEmpty(test.RightOrWrong))
                        test.RightOrWrong += "|1";
                    else
                        test.RightOrWrong = "1";
                    dbContext.SaveChanges();
                    int count = test.Questions.Split('|').ToList().Count(); //Общее количество вопросов
                    int number = test.Answers.Split('|').ToList().Count(); //Общее количество ответов

                    if (count == number) //Если ответов столько же сколько и вопросов то идем на страницу статистики.
                        return null;
                    CurrentQuestion = CountAnswers(id);
                }
            } while (questionDB == null);
            ViewBag.QID = CodeDecode(CurrentQuestion);

            var allanswers = dbContext.Answers.Find(CurrentQuestion);
            question.QuestionText = TranslitText(questionDB.QuestionText);

            if (!string.IsNullOrEmpty(questionDB.QuestionImage) && questionDB.QuestionImage != "NULL")
                question.QuestionImage = "/Images/Questions/" + questionDB.QuestionImage;

            var allAnswers = dbContext.Answers
                  .Where(u => u.IdQuestion == CurrentQuestion)
                  .Select(u => new
                  {
                      id = u.Id,
                      text = u.AnswerText
                  }).ToList();
            List<Answers> Answers = new List<Answers>();

            ViewBag.TypeQuestion = "standart";
            if (questionDB.IdCorrect[0] != '~' && questionDB.IdCorrect[0] != '#' && questionDB.IdCorrect.Split(',').Count() > 1)
                ViewBag.Multiple = "Выберите несколько вариантов ответа";
            else
                ViewBag.Multiple = "Выберите один вариант ответа";
            if (questionDB.IdCorrect[0] == '~') //Если вопрос на последовательность
                ViewBag.TypeQuestion = "sequence";
            if (questionDB.IdCorrect[0] == '#') //Если вопрос на соответствие
            {
                ViewBag.TypeQuestion = "conformity";
                string correctAnswers = questionDB.IdCorrect;
                correctAnswers = correctAnswers.Remove(0, 1);

                List<string> answDragUser = new List<string>(); //Массив двигающихся ответов
                List<string> answNoDragUser = new List<string>(); //Массив других ответов
                List<string> coupleAnsw = correctAnswers.Split(',').ToList(); //Временная переменная
                foreach (var item in coupleAnsw)
                {
                    List<string> temp = item.Split('=').ToList();
                    answDragUser.Add(temp[0]);
                    answNoDragUser.Add(temp[1]);
                }
                Shuffle(answDragUser); //Перемешиваем массив двигающихся ответов
                Shuffle(answNoDragUser); //Перемешиваем массив других ответов
                //Соединяем ответы друг за другом и помещаем в переменную ответов
                foreach (var idAnsw in answDragUser)
                    foreach (var answ in allAnswers)
                        if (Convert.ToInt32(idAnsw) == answ.id)
                            Answers.Add(new Answers { AnswerText = TranslitText(answ.text), AnswerId = CodeDecode(answ.id) });
                foreach (var idAnsw in answNoDragUser)
                    foreach (var answ in allAnswers)
                        if (Convert.ToInt32(idAnsw) == answ.id)
                            Answers.Add(new Answers { AnswerText = TranslitText(answ.text), AnswerId = CodeDecode(answ.id) });
            }
            else
            {
                for (int i = 0; i < allAnswers.Count(); i++)
                {
                    Answers.Add(new Answers { AnswerText = TranslitText(allAnswers[i].text), AnswerId = CodeDecode(allAnswers[i].id) });
                    Shuffle(Answers);
                }
            }

            questionDB.CountAll += 1; //Прибавляем +1 к тому что вопрос был задан (для статистики)
            dbContext.SaveChanges();
            question.QuestionAnswers = Answers;

            return question;
        }
        private int CountAnswers(int id)
        {
            SelfyTestDiscipline test = dbContext.SelfyTestDisciplines.Find(id);
            List<string> questions = new List<string>();
            List<string> answers = new List<string>();

            questions = test.Questions.Split('|').ToList();
            if (!string.IsNullOrEmpty(test.Answers))
                answers = test.Answers.Split('|').ToList();

            return Convert.ToInt32(questions[answers.Count()]);
        }
        private bool SaveAnswer(int id, List<int> AnswersIDs)
        {
            SelfyTestDiscipline test = dbContext.SelfyTestDisciplines.Find(id);
            int CurrentQuestion = CountAnswers(id);
            Question question = dbContext.Questions.Find(CurrentQuestion);
            string correct = question.IdCorrect;
            if (AnswersIDs != null)
            {
                if (correct[0] != '~' && correct[0] != '#') //Если обычный вопрос то сортируем ответы
                    AnswersIDs.Sort();
            }
            else
                AnswersIDs = new List<int>() { 0 };

            string answer = "";
            if (correct[0] != '#')
            {
                if (correct[0] == '~') //Если последовательность - прибавляем ~
                    answer = "~";
                for (int i = 0; i < AnswersIDs.Count(); i++)
                {
                    if (i > 0)
                        answer += "," + AnswersIDs[i];
                    else
                        answer += AnswersIDs[i];
                }
            }
            else //Если соответствие - прибавляем # и проверяем по частям на правильность ответа. 
            {
                answer = "#";
                int halfCount = AnswersIDs.Count() / 2;
                for (int i = 0; i < halfCount; i++)
                {
                    if (i > 0)
                        answer += "," + AnswersIDs[i] + "=" + AnswersIDs[i + halfCount];
                    else
                        answer += AnswersIDs[i] + "=" + AnswersIDs[i + halfCount];
                }
                List<string> tempCorrect = new List<string>();
                List<string> tempAnswer = new List<string>();
                tempCorrect = correct.Remove(0, 1).Split(',').ToList();
                tempAnswer = answer.Remove(0, 1).Split(',').ToList();
                int tempCount = 0;
                foreach (string item in tempAnswer)
                {
                    if (tempCorrect.Contains(item))
                    {
                        tempCount++;
                    }
                }
                if (tempCount == tempCorrect.Count())
                    answer = correct;
            }
            if (!string.IsNullOrEmpty(test.Answers))
                test.Answers += "|" + answer;
            else
                test.Answers += answer;

            var user = dbContext.Users.Find(User.Identity.GetUserId()); //Сохранение правильных ответов и кол-ва ответов в таблицу юзера
            user.AnswersCount += 1;
            if (correct == answer)
            {
                user.CorrectAnswersCount += 1;  //Прибавляем +1 к тому что пользователь правильно ответил (для статистики)
                question.CountCorrect += 1;  //Прибавляем +1 к тому что на вопрос правильно ответили (для статистики)

                if (!string.IsNullOrEmpty(test.RightOrWrong))
                    test.RightOrWrong += "|1";
                else
                    test.RightOrWrong = "1";
            }
            else
            {
                if (!string.IsNullOrEmpty(test.RightOrWrong))
                    test.RightOrWrong += "|0";
                else
                    test.RightOrWrong = "0";
            }

            dbContext.SaveChanges();
            return true;
        }
        static int CodeDecode(string value)
        {
            try
            {
                int randomInt = Convert.ToInt32(value.Substring(0, 2));
                switch (randomInt % 4)
                {
                    case 0:
                        {
                            string otherString = value.Substring(7, value.Length - 7);
                            var intValue10 = Convert.ToInt32(otherString, 16);
                            return intValue10 - randomInt;
                        }
                    case 1:
                        {
                            string otherString = value.Substring(2, value.Length - 5 - 2);
                            const string chars = "ABDHIJKLMN";
                            var stringValue = "";
                            for (int i = 0; i < otherString.Length; i++)
                                stringValue += chars.IndexOf(otherString[i]);
                            return Convert.ToInt32(stringValue) - randomInt;
                        }
                    case 2:
                        {
                            string otherString = value.Substring(7, value.Length - 7);
                            const string chars = "SDFGLHWXZJ";
                            var stringValue = "";
                            for (int i = 0; i < otherString.Length; i++)
                                stringValue += chars.IndexOf(otherString[i]);
                            return Convert.ToInt32(stringValue) - randomInt;
                        }
                    default:
                        {
                            string otherString = value.Substring(2, value.Length - 2);
                            const string Vowels = "AEIOU";
                            var stringValue = "";
                            for (int i = 0; i < otherString.Length; i++)
                            {
                                if (Vowels.Contains(otherString[i]))
                                    stringValue += 0;
                                else
                                    stringValue += 1;
                            }
                            var intValue10 = Convert.ToInt32(stringValue, 2);
                            return intValue10;
                        }
                }

            }
            catch (Exception)
            {
                return 0;
            }
        }
        static string CodeDecode(int intValue)
        {
            string value = intValue.ToString();
            string code = "";
            int randomInt = random.Next(10, 99);
            switch (randomInt % 4)
            {
                case 0:
                    {
                        var intValue16 = Convert.ToString(intValue + randomInt, 16);
                        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                        string randomString = new string(Enumerable.Repeat(chars, 5).Select(s => s[random.Next(s.Length)]).ToArray());
                        code = randomInt + randomString + intValue16;
                        break;
                    }
                case 1:
                    {
                        const string chars = "ABDHIJKLMN";
                        var stringValue = "";
                        value = (Convert.ToInt32(value) + randomInt).ToString();
                        for (int i = 0; i < value.Length; i++)
                            stringValue += chars[Convert.ToInt32(value[i].ToString())];
                        code = randomInt + stringValue + random.Next(10000, 99999);
                        break;
                    }
                case 2:
                    {
                        const string chars = "SDFGLHWXZJ";
                        var stringValue = "";
                        value = (Convert.ToInt32(value) + randomInt).ToString();
                        for (int i = 0; i < value.Length; i++)
                            stringValue += chars[Convert.ToInt32(value[i].ToString())];
                        code = randomInt + random.Next(10000, 99999).ToString() + stringValue;
                        break;
                    }
                default:
                    {
                        var intValue2 = Convert.ToString(intValue, 2);
                        const string Vowels = "AEIOU";
                        const string Consonants = "BCDFGHJKLMNPQRSTVWXYZ";
                        var stringValue = "";
                        for (int i = 0; i < intValue2.Length; i++)
                        {
                            if (intValue2[i] == '0')
                                stringValue += Vowels[random.Next(Vowels.Length)];
                            else
                                stringValue += Consonants[random.Next(Consonants.Length)];
                        }
                        code = randomInt + stringValue;
                        break;
                    }
            }
            return code.ToUpper();
        }
        private string TranslitText(string source)
        {
            Dictionary<string, string> dictionaryChar = new Dictionary<string, string>()
            {
                {"а","a"},
                {"е","e"},
                {"о","o"},
                {"р","p"},
                {"с","c"},
                {"х","x"},
                {"А","A"},
                {"С","C"},
                {"Р","P"},
                {"О","O"},
                {"Е","E"},
                {"М","M"},
                {"В","B"},
                {"Т","T"},
                {"Н","H"},
                {"Х","X"}
            };
            var result = "";
            foreach (var ch in source)
            {
                if (dictionaryChar.TryGetValue(ch.ToString(), out string ss))
                {
                    if (random.NextDouble() > 0.5)
                        result += ss;
                    else
                        result += ch;
                }
                else result += ch;
            }
            return result;
        }
        #endregion
    }
}