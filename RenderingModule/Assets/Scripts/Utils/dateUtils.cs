// Source: https://stackoverflow.com/a/5097066
using System;
using UnityEngine;

namespace DateUtils
{
    public static class DateUtil
    {
        public static DateTime DefaultStartDate = new DateTime(2018, 1, 1);
        public static DateTime DefaultEndDate = new DateTime(2018, 7, 3);
        public static string Float2DateStr(float targetNum, DateTime startDate, DateTime endDate)
        {
            if(targetNum > 1 || targetNum < 0)
            {
                throw new Exception("targetNum must be between 0 and 1");
            }

            TimeSpan timeSpan = endDate - startDate;
            int totalDays = timeSpan.Days;
            DateTime targetDate = startDate.AddDays(totalDays * targetNum);
            int Year = targetDate.Year;
            int Month = targetDate.Month;
            int Day = targetDate.Day;
            return Year.ToString() + "-" + Month.ToString() + "-" + Day.ToString();
        }

        public static string getDateStrByIndex(int index, DateTime startDate, DateTime endDate, int totalIndex)
        {
            //DateTime targetDate = startDate.AddDays(index);
            DateTime targetDate = startDate.AddDays(((endDate - startDate) / totalIndex * (index + 1)).TotalDays);
            return getDateStrByDateTime(targetDate);
        }

        public static string getDateStrByFloat(float indexFloat, DateTime startDate, DateTime endDate, int totalIndex)
        {
            //DateTime targetDate = startDate.AddDays(index);
            int index = Mathf.FloorToInt(totalIndex * indexFloat);
            DateTime targetDate = startDate.AddDays(((endDate - startDate) / totalIndex * (index + 1)).TotalDays);
            return getDateStrByDateTime(targetDate);
        }

        public static DateTime getDateTimeByFloat(float indexFloat, DateTime startDate, DateTime endDate, int totalIndex)
        {
            int index = Mathf.FloorToInt(totalIndex * indexFloat);
            DateTime targetDate = startDate.AddDays(((endDate - startDate) / totalIndex * (index + 1)).TotalDays);
            return targetDate;
        }

        public static string getDateStrByDateTime(DateTime targetDate)
        {
            int Year = targetDate.Year;
            int Month = targetDate.Month;
            int Day = targetDate.Day;
            return Year.ToString() + "-" + Month.ToString() + "-" + Day.ToString();
        }

        public static int getDayRangeByDateTime(DateTime startDate, DateTime endDate)
        {
            double totalDays = (endDate - startDate).TotalDays;
            return (int)totalDays;
        }
    }
}
