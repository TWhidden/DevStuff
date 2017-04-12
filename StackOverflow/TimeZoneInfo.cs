using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security;
#if !PCL
using System.Security.Permissions;
#endif
using System.Text;
using System.Threading;


namespace HJT.NVR.ServiceTypes.v1_0.Entities
{
    public sealed class TimeZoneInfo : IEquatable<TimeZoneInfo>
    {
        // Fields
        private const string c_daylightValue = "Dlt";
        private const string c_disableDST = "DisableAutoDaylightTimeSet";
        private const string c_disableDynamicDST = "DynamicDaylightTimeDisabled";
        private const string c_displayValue = "Display";
        private const string c_firstEntryValue = "FirstEntry";
        private const string c_lastEntryValue = "LastEntry";
        private const string c_localId = "Local";
        private const int c_maxKeyLength = 0xff;
        private const string c_muiDaylightValue = "MUI_Dlt";
        private const string c_muiDisplayValue = "MUI_Display";
        private const string c_muiStandardValue = "MUI_Std";
        private const string c_standardValue = "Std";
        private const long c_ticksPerDay = 0xc92a69c000L;
        private const long c_ticksPerDayRange = 0xc92a6998f0L;
        private const long c_ticksPerHour = 0x861c46800L;
        private const long c_ticksPerMillisecond = 0x2710L;
        private const long c_ticksPerMinute = 0x23c34600L;
        private const long c_ticksPerSecond = 0x989680L;
        private const string c_timeZoneInfoRegistryHive = @"SYSTEM\CurrentControlSet\Control\TimeZoneInformation";
        private const string c_timeZoneInfoRegistryHivePermissionList = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\TimeZoneInformation";
        private const string c_timeZoneInfoValue = "TZI";
        private const string c_timeZonesRegistryHive = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones";
        private const string c_timeZonesRegistryHivePermissionList = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones";
        private const string c_utcId = "UTC";
        private AdjustmentRule[] m_adjustmentRules;
        private TimeSpan m_baseUtcOffset;
        private string m_daylightDisplayName;
        private string m_displayName;
        private string m_id;
        private string m_standardDisplayName;
        private bool m_supportsDaylightSavingTime;
        private static bool s_allSystemTimeZonesRead = false;
        private static object s_hiddenInternalSyncObject;
        private static Dictionary<string, TimeZoneInfo> s_hiddenSystemTimeZones;
        private static TimeZoneInfo s_localTimeZone;
        private static TimeZoneInfo s_utcTimeZone;


        private TimeZoneInfo(string id, TimeSpan baseUtcOffset, string displayName, string standardDisplayName, string daylightDisplayName, AdjustmentRule[] adjustmentRules, bool disableDaylightSavingTime)
        {
            bool flag;
            ValidateTimeZoneInfo(id, baseUtcOffset, adjustmentRules, out flag);
            if ((!disableDaylightSavingTime && (adjustmentRules != null)) && (adjustmentRules.Length > 0))
            {
                this.m_adjustmentRules = (AdjustmentRule[])adjustmentRules.Clone();
            }
            this.m_id = id;
            this.m_baseUtcOffset = baseUtcOffset;
            this.m_displayName = displayName;
            this.m_standardDisplayName = standardDisplayName;
            this.m_daylightDisplayName = disableDaylightSavingTime ? null : daylightDisplayName;
            this.m_supportsDaylightSavingTime = flag && !disableDaylightSavingTime;
        }

        private static bool CheckIsDst(DateTime startTime, DateTime time, DateTime endTime)
        {
            if (startTime.Year != endTime.Year)
            {
                endTime = endTime.AddYears(startTime.Year - endTime.Year);
            }
            if (startTime.Year != time.Year)
            {
                time = time.AddYears(startTime.Year - time.Year);
            }
            if (startTime > endTime)
            {
                return ((time < endTime) || (time >= startTime));
            }
            return ((time >= startTime) && (time < endTime));
        }

        public static void ClearCachedData()
        {
            lock (s_internalSyncObject)
            {
                s_localTimeZone = null;
                s_utcTimeZone = null;
                s_systemTimeZones = null;
                s_allSystemTimeZonesRead = false;
            }
        }

        public static DateTime ConvertTime(DateTime dateTime, TimeZoneInfo destinationTimeZone)
        {
            if (destinationTimeZone == null)
            {
                throw new ArgumentNullException("destinationTimeZone");
            }
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                lock (s_internalSyncObject)
                {
                    return ConvertTime(dateTime, Utc, destinationTimeZone);
                }
            }
            lock (s_internalSyncObject)
            {
                return ConvertTime(dateTime, Local, destinationTimeZone);
            }
        }

        public static DateTimeOffset ConvertTime(DateTimeOffset dateTimeOffset, TimeZoneInfo destinationTimeZone)
        {
            if (destinationTimeZone == null)
            {
                throw new ArgumentNullException("destinationTimeZone");
            }
            DateTime utcDateTime = dateTimeOffset.UtcDateTime;
            TimeSpan utcOffsetFromUtc = GetUtcOffsetFromUtc(utcDateTime, destinationTimeZone);
            long ticks = utcDateTime.Ticks + utcOffsetFromUtc.Ticks;
            if (ticks > DateTimeOffset.MaxValue.Ticks)
            {
                return DateTimeOffset.MaxValue;
            }
            if (ticks < DateTimeOffset.MinValue.Ticks)
            {
                return DateTimeOffset.MinValue;
            }
            return new DateTimeOffset(ticks, utcOffsetFromUtc);
        }

        public static DateTime ConvertTime(DateTime dateTime, TimeZoneInfo sourceTimeZone, TimeZoneInfo destinationTimeZone)
        {
            return ConvertTime(dateTime, sourceTimeZone, destinationTimeZone, TimeZoneInfoOptions.None);
        }

        internal static DateTime ConvertTime(DateTime dateTime, TimeZoneInfo sourceTimeZone, TimeZoneInfo destinationTimeZone, TimeZoneInfoOptions flags)
        {
            if (sourceTimeZone == null)
            {
                throw new ArgumentNullException("sourceTimeZone");
            }
            if (destinationTimeZone == null)
            {
                throw new ArgumentNullException("destinationTimeZone");
            }
            DateTimeKind correspondingKind = sourceTimeZone.GetCorrespondingKind();
            if ((((flags & TimeZoneInfoOptions.NoThrowOnInvalidTime) == 0) && (dateTime.Kind != DateTimeKind.Unspecified)) && (dateTime.Kind != correspondingKind))
            {
                //throw new ArgumentException(SR.GetString("Argument_ConvertMismatch"), "sourceTimeZone");
            }
            AdjustmentRule adjustmentRuleForTime = sourceTimeZone.GetAdjustmentRuleForTime(dateTime);
            TimeSpan baseUtcOffset = sourceTimeZone.BaseUtcOffset;
            if (adjustmentRuleForTime != null)
            {
                bool flag = false;
                DaylightTime daylightTime = GetDaylightTime(dateTime.Year, adjustmentRuleForTime);
                if (((flags & TimeZoneInfoOptions.NoThrowOnInvalidTime) == 0) && GetIsInvalidTime(dateTime, adjustmentRuleForTime, daylightTime))
                {
                    //throw new ArgumentException(SR.GetString("Argument_DateTimeIsInvalid"), "dateTime");
                }
                flag = GetIsDaylightSavings(dateTime, adjustmentRuleForTime, daylightTime);
                baseUtcOffset += flag ? adjustmentRuleForTime.DaylightDelta : TimeSpan.Zero;
            }
            DateTimeKind kind = destinationTimeZone.GetCorrespondingKind();
            if (((dateTime.Kind != DateTimeKind.Unspecified) && (correspondingKind != DateTimeKind.Unspecified)) && (correspondingKind == kind))
            {
                return dateTime;
            }
            long ticks = dateTime.Ticks - baseUtcOffset.Ticks;
            DateTime time2 = ConvertUtcToTimeZone(ticks, destinationTimeZone);
            if (kind == DateTimeKind.Local)
            {
                kind = DateTimeKind.Unspecified;
            }
            return new DateTime(time2.Ticks, kind);
        }

        public static DateTime ConvertTimeBySystemTimeZoneId(DateTime dateTime, string destinationTimeZoneId)
        {
            return ConvertTime(dateTime, FindSystemTimeZoneById(destinationTimeZoneId));
        }

        public static DateTimeOffset ConvertTimeBySystemTimeZoneId(DateTimeOffset dateTimeOffset, string destinationTimeZoneId)
        {
            return ConvertTime(dateTimeOffset, FindSystemTimeZoneById(destinationTimeZoneId));
        }

        public static DateTime ConvertTimeBySystemTimeZoneId(DateTime dateTime, string sourceTimeZoneId, string destinationTimeZoneId)
        {
            if ((dateTime.Kind == DateTimeKind.Local) && (string.Compare(sourceTimeZoneId, Local.Id, StringComparison.OrdinalIgnoreCase) == 0))
            {
                lock (s_internalSyncObject)
                {
                    return ConvertTime(dateTime, Local, FindSystemTimeZoneById(destinationTimeZoneId));
                }
            }
            if ((dateTime.Kind == DateTimeKind.Utc) && (string.Compare(sourceTimeZoneId, Utc.Id, StringComparison.OrdinalIgnoreCase) == 0))
            {
                lock (s_internalSyncObject)
                {
                    return ConvertTime(dateTime, Utc, FindSystemTimeZoneById(destinationTimeZoneId));
                }
            }
            return ConvertTime(dateTime, FindSystemTimeZoneById(sourceTimeZoneId), FindSystemTimeZoneById(destinationTimeZoneId));
        }

        public static DateTime ConvertTimeFromUtc(DateTime dateTime, TimeZoneInfo destinationTimeZone)
        {
            lock (s_internalSyncObject)
            {
                return ConvertTime(dateTime, Utc, destinationTimeZone);
            }
        }

        public static DateTime ConvertTimeToUtc(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                return dateTime;
            }
            lock (s_internalSyncObject)
            {
                return ConvertTime(dateTime, Local, Utc);
            }
        }

        public static DateTime ConvertTimeToUtc(DateTime dateTime, TimeZoneInfo sourceTimeZone)
        {
            lock (s_internalSyncObject)
            {
                return ConvertTime(dateTime, sourceTimeZone, Utc);
            }
        }

        private static DateTime ConvertUtcToTimeZone(long ticks, TimeZoneInfo destinationTimeZone)
        {
            DateTime maxValue;
            if (ticks > DateTime.MaxValue.Ticks)
            {
                maxValue = DateTime.MaxValue;
            }
            else if (ticks < DateTime.MinValue.Ticks)
            {
                maxValue = DateTime.MinValue;
            }
            else
            {
                maxValue = new DateTime(ticks);
            }
            TimeSpan utcOffsetFromUtc = GetUtcOffsetFromUtc(maxValue, destinationTimeZone);
            ticks += utcOffsetFromUtc.Ticks;
            if (ticks > DateTime.MaxValue.Ticks)
            {
                return DateTime.MaxValue;
            }
            if (ticks < DateTime.MinValue.Ticks)
            {
                return DateTime.MinValue;
            }
            return new DateTime(ticks);
        }

        

        public static TimeZoneInfo CreateCustomTimeZone(string id, TimeSpan baseUtcOffset, string displayName, string standardDisplayName)
        {
            return new TimeZoneInfo(id, baseUtcOffset, displayName, standardDisplayName, standardDisplayName, null, false);
        }

        public static TimeZoneInfo CreateCustomTimeZone(string id, TimeSpan baseUtcOffset, string displayName, string standardDisplayName, string daylightDisplayName, AdjustmentRule[] adjustmentRules)
        {
            return new TimeZoneInfo(id, baseUtcOffset, displayName, standardDisplayName, daylightDisplayName, adjustmentRules, false);
        }

        public static TimeZoneInfo CreateCustomTimeZone(string id, TimeSpan baseUtcOffset, string displayName, string standardDisplayName, string daylightDisplayName, AdjustmentRule[] adjustmentRules, bool disableDaylightSavingTime)
        {
            return new TimeZoneInfo(id, baseUtcOffset, displayName, standardDisplayName, daylightDisplayName, adjustmentRules, disableDaylightSavingTime);
        }

        public bool Equals(TimeZoneInfo other)
        {
            return (((other != null) && (string.Compare(this.m_id, other.m_id, StringComparison.OrdinalIgnoreCase) == 0)) && this.HasSameRules(other));
        }

       

        public static TimeZoneInfo FindSystemTimeZoneById(string id)
        {
            if (string.Compare(id, "UTC", StringComparison.OrdinalIgnoreCase) == 0)
            {
                return Utc;
            }
            lock (s_internalSyncObject)
            {
                return GetTimeZone(id);
            }
        }

        public static TimeZoneInfo FromSerializedString(string source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (source.Length == 0)
            {
                //throw new ArgumentException(SR.GetString("Argument_InvalidSerializedString", new object[] { source }), "source");
            }
            return StringSerializer.GetDeserializedTimeZoneInfo(source);
        }

        private AdjustmentRule GetAdjustmentRuleForTime(DateTime dateTime)
        {
            if ((this.m_adjustmentRules != null) && (this.m_adjustmentRules.Length != 0))
            {
                DateTime date = dateTime.Date;
                for (int i = 0; i < this.m_adjustmentRules.Length; i++)
                {
                    if ((this.m_adjustmentRules[i].DateStart <= date) && (this.m_adjustmentRules[i].DateEnd >= date))
                    {
                        return this.m_adjustmentRules[i];
                    }
                }
            }
            return null;
        }

        public AdjustmentRule[] GetAdjustmentRules()
        {
            if (this.m_adjustmentRules == null)
            {
                return new AdjustmentRule[0];
            }
            return (AdjustmentRule[])this.m_adjustmentRules.Clone();
        }

        public TimeSpan[] GetAmbiguousTimeOffsets(DateTime dateTime)
        {
            DateTime time;
            bool flag;
            if (!this.m_supportsDaylightSavingTime)
            {
                //throw new ArgumentException(SR.GetString("Argument_DateTimeIsNotAmbiguous"), "dateTime");
            }
            if (dateTime.Kind == DateTimeKind.Local)
            {
                lock (s_internalSyncObject)
                {
                    time = ConvertTime(dateTime, Local, this, TimeZoneInfoOptions.NoThrowOnInvalidTime);
                    goto Label_007D;
                }
            }
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                lock (s_internalSyncObject)
                {
                    time = ConvertTime(dateTime, Utc, this, TimeZoneInfoOptions.NoThrowOnInvalidTime);
                    goto Label_007D;
                }
            }
            time = dateTime;
        Label_007D:
            flag = false;
            AdjustmentRule adjustmentRuleForTime = this.GetAdjustmentRuleForTime(time);
            if (adjustmentRuleForTime != null)
            {
                DaylightTime daylightTime = GetDaylightTime(time.Year, adjustmentRuleForTime);
                flag = GetIsAmbiguousTime(time, adjustmentRuleForTime, daylightTime);
            }
            if (!flag)
            {
                //throw new ArgumentException(SR.GetString("Argument_DateTimeIsNotAmbiguous"), "dateTime");
            }
            TimeSpan[] spanArray = new TimeSpan[2];
            if (adjustmentRuleForTime.DaylightDelta > TimeSpan.Zero)
            {
                spanArray[0] = this.m_baseUtcOffset;
                spanArray[1] = this.m_baseUtcOffset + adjustmentRuleForTime.DaylightDelta;
                return spanArray;
            }
            spanArray[0] = this.m_baseUtcOffset + adjustmentRuleForTime.DaylightDelta;
            spanArray[1] = this.m_baseUtcOffset;
            return spanArray;
        }

        public TimeSpan[] GetAmbiguousTimeOffsets(DateTimeOffset dateTimeOffset)
        {
            if (!this.m_supportsDaylightSavingTime)
            {
                //throw new ArgumentException(SR.GetString("Argument_DateTimeOffsetIsNotAmbiguous"), "dateTimeOffset");
            }
            DateTime dateTime = ConvertTime(dateTimeOffset, this).DateTime;
            bool flag = false;
            AdjustmentRule adjustmentRuleForTime = this.GetAdjustmentRuleForTime(dateTime);
            if (adjustmentRuleForTime != null)
            {
                DaylightTime daylightTime = GetDaylightTime(dateTime.Year, adjustmentRuleForTime);
                flag = GetIsAmbiguousTime(dateTime, adjustmentRuleForTime, daylightTime);
            }
            if (!flag)
            {
                //throw new ArgumentException(SR.GetString("Argument_DateTimeOffsetIsNotAmbiguous"), "dateTimeOffset");
            }
            TimeSpan[] spanArray = new TimeSpan[2];
            if (adjustmentRuleForTime.DaylightDelta > TimeSpan.Zero)
            {
                spanArray[0] = this.m_baseUtcOffset;
                spanArray[1] = this.m_baseUtcOffset + adjustmentRuleForTime.DaylightDelta;
                return spanArray;
            }
            spanArray[0] = this.m_baseUtcOffset + adjustmentRuleForTime.DaylightDelta;
            spanArray[1] = this.m_baseUtcOffset;
            return spanArray;
        }

        private DateTimeKind GetCorrespondingKind()
        {
            if (this == s_utcTimeZone)
            {
                return DateTimeKind.Utc;
            }
            if (this == s_localTimeZone)
            {
                return DateTimeKind.Local;
            }
            return DateTimeKind.Unspecified;
        }

        private static DaylightTime GetDaylightTime(int year, AdjustmentRule rule)
        {
            TimeSpan daylightDelta = rule.DaylightDelta;
            DateTime start = TransitionTimeToDateTime(year, rule.DaylightTransitionStart);
            return new DaylightTime(start, TransitionTimeToDateTime(year, rule.DaylightTransitionEnd), daylightDelta);
        }

        public override int GetHashCode()
        {
            return this.m_id.ToUpper().GetHashCode();
        }

        private static bool GetIsAmbiguousTime(DateTime time, AdjustmentRule rule, DaylightTime daylightTime)
        {
            bool flag = false;
            if ((rule != null) && (rule.DaylightDelta != TimeSpan.Zero))
            {
                DateTime end;
                DateTime time3;
                DateTime time4;
                DateTime time5;
                if (rule.DaylightDelta > TimeSpan.Zero)
                {
                    end = daylightTime.End;
                    time3 = daylightTime.End - rule.DaylightDelta;
                }
                else
                {
                    end = daylightTime.Start;
                    time3 = daylightTime.Start + rule.DaylightDelta;
                }
                flag = (time >= time3) && (time < end);
                if (flag || (end.Year == time3.Year))
                {
                    return flag;
                }
                try
                {
                    time4 = end.AddYears(1);
                    time5 = time3.AddYears(1);
                    flag = (time >= time5) && (time < time4);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
                if (flag)
                {
                    return flag;
                }
                try
                {
                    time4 = end.AddYears(-1);
                    time5 = time3.AddYears(-1);
                    flag = (time >= time5) && (time < time4);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
            return flag;
        }

        private static bool GetIsDaylightSavings(DateTime time, AdjustmentRule rule, DaylightTime daylightTime)
        {
            if (rule == null)
            {
                return false;
            }
            bool flag = rule.DaylightDelta > TimeSpan.Zero;
            DateTime startTime = daylightTime.Start + (flag ? rule.DaylightDelta : TimeSpan.Zero);
            DateTime endTime = daylightTime.End + (flag ? rule.DaylightDelta.Negate() : TimeSpan.Zero);
            return CheckIsDst(startTime, time, endTime);
        }

        private static bool GetIsDaylightSavingsFromUtc(DateTime time, int Year, TimeSpan utc, AdjustmentRule rule)
        {
            if (rule == null)
            {
                return false;
            }
            TimeSpan span = utc;
            DaylightTime daylightTime = GetDaylightTime(Year, rule);
            DateTime startTime = daylightTime.Start - span;
            DateTime endTime = (daylightTime.End - span) - rule.DaylightDelta;
            return CheckIsDst(startTime, time, endTime);
        }

        private static bool GetIsInvalidTime(DateTime time, AdjustmentRule rule, DaylightTime daylightTime)
        {
            bool flag = false;
            if ((rule != null) && (rule.DaylightDelta != TimeSpan.Zero))
            {
                DateTime end;
                DateTime time3;
                DateTime time4;
                DateTime time5;
                if (rule.DaylightDelta < TimeSpan.Zero)
                {
                    end = daylightTime.End;
                    time3 = daylightTime.End - rule.DaylightDelta;
                }
                else
                {
                    end = daylightTime.Start;
                    time3 = daylightTime.Start + rule.DaylightDelta;
                }
                flag = (time >= end) && (time < time3);
                if (flag || (end.Year == time3.Year))
                {
                    return flag;
                }
                try
                {
                    time4 = end.AddYears(1);
                    time5 = time3.AddYears(1);
                    flag = (time >= time4) && (time < time5);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
                if (flag)
                {
                    return flag;
                }
                try
                {
                    time4 = end.AddYears(-1);
                    time5 = time3.AddYears(-1);
                    flag = (time >= time4) && (time < time5);
                }
                catch (ArgumentOutOfRangeException)
                {
                }
            }
            return flag;
        }



        private struct CFRange
        {
            private int location;
            private int length;
            public CFRange(int location, int length)
            {
                this.location = location;
                this.length = length;
            }
        }

        private struct TZifHead
        {
            private const int c_len = 0x2c;
            public uint Magic;
            public uint IsGmtCount;
            public uint IsStdCount;
            public uint LeapCount;
            public uint TimeCount;
            public uint TypeCount;
            public uint CharCount;
            public static int Length
            {
                get
                {
                    return 0x2c;
                }
            }
            [SecurityCritical]
            public TZifHead(byte[] data, int index)
            {
                if ((data == null) || (data.Length < 0x2c))
                {
                    throw new ArgumentException("bad data", "data");
                }
                this.Magic = (uint)TimeZoneInfo.TZif_ToInt32(data, index);
                if (this.Magic != 0x545a6966)
                {
                    //throw new ArgumentException(SR.GetString("Argument_TimeZoneInfoBadTZif"), "data");
                }
                this.IsGmtCount = (uint)TimeZoneInfo.TZif_ToInt32(data, index + 20);
                this.IsStdCount = (uint)TimeZoneInfo.TZif_ToInt32(data, index + 0x18);
                this.LeapCount = (uint)TimeZoneInfo.TZif_ToInt32(data, index + 0x1c);
                this.TimeCount = (uint)TimeZoneInfo.TZif_ToInt32(data, index + 0x20);
                this.TypeCount = (uint)TimeZoneInfo.TZif_ToInt32(data, index + 0x24);
                this.CharCount = (uint)TimeZoneInfo.TZif_ToInt32(data, index + 40);
            }
        }

        private static TransitionTime TZif_CalculateTransitionTime(DateTime utc, TimeSpan offset, TZifType transitionType, bool standardTime, bool gmtTime, out DateTime ruleDate)
        {
            long ticks = utc.Ticks + offset.Ticks;
            if (ticks > DateTime.MaxValue.Ticks)
            {
                utc = DateTime.MaxValue;
            }
            else if (ticks < DateTime.MinValue.Ticks)
            {
                utc = DateTime.MinValue;
            }
            else
            {
                utc = new DateTime(ticks);
            }
            DateTime timeOfDay = new DateTime(1, 1, 1, utc.Hour, utc.Minute, utc.Second, utc.Millisecond);
            int month = utc.Month;
            int day = utc.Day;
            ruleDate = new DateTime(utc.Year, utc.Month, utc.Day);
            return TransitionTime.CreateFixedDateRule(timeOfDay, month, day);
        }

        private static readonly object c_transition10_15 = TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 10, 15);
        private static readonly object c_transition12_15 = TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 12, 15);
        private static readonly object c_transition5_15 = TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 5, 15);
        private static readonly object c_transition7_15 = TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 7, 15);


        private static void TZif_CreateFirstMultiYearRule(ref List<AdjustmentRule> rulesList, TimeSpan daylightBias, DateTime startTransitionDate, int DstStartIndex, int dstStartTypeIndex, int dstEndTypeIndex, DateTime[] dts, TZifType[] transitionType, byte[] typeOfLocalTime, bool[] StandardTime, bool[] GmtTime)
        {
            DateTime time;
            DateTime maxValue;
            TransitionTime time4;
            TimeSpan offset = (DstStartIndex > 0) ? transitionType[typeOfLocalTime[DstStartIndex - 1]].UtcOffset : transitionType[dstEndTypeIndex].UtcOffset;
            TransitionTime daylightTransitionStart = TZif_CalculateTransitionTime(startTransitionDate, offset, transitionType[dstStartTypeIndex], StandardTime[dstStartTypeIndex], GmtTime[dstStartTypeIndex], out time);
            if (time.Month <= 4)
            {
                maxValue = new DateTime(time.Year, 6, 15);
                time4 = (TransitionTime)c_transition7_15;
            }
            else if (time.Month <= 8)
            {
                maxValue = new DateTime(time.Year, 11, 15);
                time4 = (TransitionTime)c_transition12_15;
            }
            else if (time.Year < 0x270f)
            {
                maxValue = new DateTime(time.Year + 1, 6, 15);
                time4 = (TransitionTime)c_transition7_15;
            }
            else
            {
                maxValue = DateTime.MaxValue;
                time4 = (TransitionTime)c_transition7_15;
            }
            AdjustmentRule item = AdjustmentRule.CreateAdjustmentRule(time, maxValue, daylightBias, daylightTransitionStart, time4);
            rulesList.Add(item);
        }

        private static void TZif_CreateLastMultiYearRule(ref List<AdjustmentRule> rulesList, TimeSpan daylightBias, DateTime endTransitionDate, int DstStartIndex, int dstStartTypeIndex, int DstEndIndex, int dstEndTypeIndex, DateTime[] dts, TZifType[] transitionType, byte[] typeOfLocalTime, bool[] StandardTime, bool[] GmtTime)
        {
            DateTime date;
            TimeSpan offset = (DstEndIndex > 0) ? transitionType[typeOfLocalTime[DstEndIndex - 1]].UtcOffset : transitionType[dstStartTypeIndex].UtcOffset;
            TransitionTime daylightTransitionEnd = TZif_CalculateTransitionTime(endTransitionDate, offset, transitionType[dstEndTypeIndex], StandardTime[dstEndTypeIndex], GmtTime[dstEndTypeIndex], out date);
            if (DstStartIndex >= DstEndIndex)
            {
                date = DateTime.MaxValue.Date;
            }
            AdjustmentRule rule = rulesList[rulesList.Count - 1];
            int year = rule.DateEnd.Year;
            if (rule.DateEnd.Month <= 6)
            {
                AdjustmentRule item = AdjustmentRule.CreateAdjustmentRule(new DateTime(year, 6, 0x10), date, daylightBias, (TransitionTime)c_transition5_15, daylightTransitionEnd);
                rulesList.Add(item);
            }
            else
            {
                AdjustmentRule rule3 = AdjustmentRule.CreateAdjustmentRule(new DateTime(year, 11, 0x10), date, daylightBias, (TransitionTime)c_transition10_15, daylightTransitionEnd);
                rulesList.Add(rule3);
            }
        }

        private static void TZif_CreateMiddleMultiYearRules(ref List<AdjustmentRule> rulesList, TimeSpan daylightBias, DateTime endTransitionDate)
        {
            DateTime time;
            AdjustmentRule rule = rulesList[rulesList.Count - 1];
            if (endTransitionDate.Month <= 8)
            {
                time = new DateTime(endTransitionDate.Year - 1, 11, 15);
            }
            else
            {
                time = new DateTime(endTransitionDate.Year, 6, 15);
            }
            while (rule.DateEnd < time)
            {
                int year = rule.DateEnd.Year;
                if (rule.DateEnd.Month <= 6)
                {
                    AdjustmentRule item = AdjustmentRule.CreateAdjustmentRule(new DateTime(year, 6, 0x10), new DateTime(year, 11, 15), daylightBias, (TransitionTime)c_transition5_15, (TransitionTime)c_transition12_15);
                    rule = item;
                    rulesList.Add(item);
                }
                else
                {
                    AdjustmentRule rule3 = AdjustmentRule.CreateAdjustmentRule(new DateTime(year, 11, 0x10), new DateTime(year + 1, 6, 15), daylightBias, (TransitionTime)c_transition10_15, (TransitionTime)c_transition7_15);
                    rule = rule3;
                    rulesList.Add(rule3);
                }
            }
        }

        private static bool TZif_GenerateAdjustmentRule(ref int startIndex, ref List<AdjustmentRule> rulesList, DateTime[] dts, byte[] typeOfLocalTime, TZifType[] transitionType, bool[] StandardTime, bool[] GmtTime)
        {
            int index = startIndex;
            bool flag = false;
            int num2 = -1;
            int num3 = -1;
            DateTime date = DateTime.MinValue.Date;
            DateTime ruleDate = DateTime.MaxValue.Date;
            while (!flag && (index < typeOfLocalTime.Length))
            {
                int num4 = typeOfLocalTime[index];
                if ((num4 < transitionType.Length) && transitionType[num4].IsDst)
                {
                    flag = true;
                    num2 = index;
                }
                else
                {
                    index++;
                }
            }
            while (flag && (index < typeOfLocalTime.Length))
            {
                int num5 = typeOfLocalTime[index];
                if ((num5 < transitionType.Length) && !transitionType[num5].IsDst)
                {
                    flag = false;
                    num3 = index;
                }
                else
                {
                    index++;
                }
            }
            if (num2 >= 0)
            {
                DateTime maxValue;
                DateTime utc = dts[num2];
                if (num3 == -1)
                {
                    if (num2 > 0)
                    {
                        num3 = num2 - 1;
                    }
                    else
                    {
                        num3 = num2;
                    }
                    maxValue = DateTime.MaxValue;
                }
                else
                {
                    maxValue = dts[num3];
                }
                int num6 = typeOfLocalTime[num2];
                int num7 = typeOfLocalTime[num3];
                TimeSpan daylightDelta = transitionType[num6].UtcOffset - transitionType[num7].UtcOffset;
                if ((daylightDelta.Ticks % 0x23c34600L) != 0L)
                {
                    daylightDelta = new TimeSpan(daylightDelta.Hours, daylightDelta.Minutes, 0);
                }
                TimeSpan span4 = (TimeSpan)(maxValue - utc);
                if (span4.Ticks <= 0x11e084e5d0000L)
                {
                    TimeSpan offset = (num2 > 0) ? transitionType[typeOfLocalTime[num2 - 1]].UtcOffset : transitionType[num7].UtcOffset;
                    TimeSpan span3 = (num3 > 0) ? transitionType[typeOfLocalTime[num3 - 1]].UtcOffset : transitionType[num6].UtcOffset;
                    TransitionTime daylightTransitionStart = TZif_CalculateTransitionTime(utc, offset, transitionType[num6], StandardTime[num6], GmtTime[num6], out date);
                    TransitionTime daylightTransitionEnd = TZif_CalculateTransitionTime(maxValue, span3, transitionType[num7], StandardTime[num7], GmtTime[num7], out ruleDate);
                    if (num2 >= num3)
                    {
                        ruleDate = DateTime.MaxValue.Date;
                    }
                    AdjustmentRule item = AdjustmentRule.CreateAdjustmentRule(date, ruleDate, daylightDelta, daylightTransitionStart, daylightTransitionEnd);
                    rulesList.Add(item);
                }
                else
                {
                    TZif_CreateFirstMultiYearRule(ref rulesList, daylightDelta, utc, num2, num6, num7, dts, transitionType, typeOfLocalTime, StandardTime, GmtTime);
                    TZif_CreateMiddleMultiYearRules(ref rulesList, daylightDelta, maxValue);
                    TZif_CreateLastMultiYearRule(ref rulesList, daylightDelta, maxValue, num2, num6, num3, num7, dts, transitionType, typeOfLocalTime, StandardTime, GmtTime);
                }
                startIndex = index + 1;
                return true;
            }
            startIndex = index + 1;
            return false;
        }

        private static void TZif_GenerateAdjustmentRules(out AdjustmentRule[] rules, DateTime[] dts, byte[] typeOfLocalTime, TZifType[] transitionType, bool[] StandardTime, bool[] GmtTime)
        {
            rules = null;
            int startIndex = 0;
            List<AdjustmentRule> rulesList = new List<AdjustmentRule>(1);
            for (bool flag = true; flag && (startIndex < dts.Length); flag = TZif_GenerateAdjustmentRule(ref startIndex, ref rulesList, dts, typeOfLocalTime, transitionType, StandardTime, GmtTime))
            {
            }
            rules = rulesList.ToArray();
            if ((rules != null) && (rules.Length == 0))
            {
                rules = null;
            }
        }

        [SecurityCritical]
        private static string TZif_GetZoneAbbreviation(string zoneAbbreviations, int index)
        {
            int num = zoneAbbreviations.IndexOf('\0', index);
            if (num > 0)
            {
                return zoneAbbreviations.Substring(index, num - index);
            }
            return zoneAbbreviations.Substring(index);
        }

        [SecurityCritical]
        private static void TZif_ParseRaw(byte[] data, out TZifHead t, out DateTime[] dts, out byte[] typeOfLocalTime, out TZifType[] transitionType, out string zoneAbbreviations, out bool[] StandardTime, out bool[] GmtTime)
        {
            dts = null;
            typeOfLocalTime = null;
            transitionType = null;
            zoneAbbreviations = string.Empty;
            StandardTime = null;
            GmtTime = null;
            t = new TZifHead(data, 0);
            int length = TZifHead.Length;
            dts = new DateTime[t.TimeCount];
            typeOfLocalTime = new byte[t.TimeCount];
            transitionType = new TZifType[t.TypeCount];
            zoneAbbreviations = string.Empty;
            StandardTime = new bool[t.TypeCount];
            GmtTime = new bool[t.TypeCount];
            for (int i = 0; i < t.TimeCount; i++)
            {
                int unixTime = TZif_ToInt32(data, length);
                dts[i] = TZif_UnixTimeToWindowsTime(unixTime);
                length += 4;
            }
            for (int j = 0; j < t.TimeCount; j++)
            {
                typeOfLocalTime[j] = data[length];
                length++;
            }
            for (int k = 0; k < t.TypeCount; k++)
            {
                transitionType[k] = new TZifType(data, length);
                length += 6;
            }
            zoneAbbreviations = new UTF8Encoding().GetString(data, length, (int)t.CharCount);
            length += (int)t.CharCount;
            length += (int)(t.LeapCount * 8);
            for (int m = 0; ((m < t.IsStdCount) && (m < t.TypeCount)) && (length < data.Length); m++)
            {
                StandardTime[m] = data[length++] != 0;
            }
            for (int n = 0; ((n < t.IsGmtCount) && (n < t.TypeCount)) && (length < data.Length); n++)
            {
                GmtTime[n] = data[length++] != 0;
            }
        }

        private struct TZifType
        {
            private const int c_len = 6;
            public TimeSpan UtcOffset;
            public bool IsDst;
            public byte AbbreviationIndex;
            [SecurityCritical]
            public TZifType(byte[] data, int index)
            {
                if ((data == null) || (data.Length < (index + 6)))
                {
                    //throw new ArgumentException(SR.GetString("Argument_TimeZoneInfoInvalidTZif"), "data");
                }
                this.UtcOffset = new TimeSpan(0, 0, TimeZoneInfo.TZif_ToInt32(data, index));
                this.IsDst = data[index + 4] != 0;
                this.AbbreviationIndex = data[index + 5];
            }
        }


        [SecurityCritical]
        private static int TZif_ToInt32(byte[] value, int startIndex)
        {
            //byte[] someBytes = new byte[4];
            //someBytes[0] = value[startIndex];
            //someBytes[1] = value[startIndex++];
            //someBytes[2] = value[startIndex++];
            //someBytes[3] = value[startIndex++];

            return ((((value[startIndex] << 0x18) | (value[startIndex++] << 0x10)) | (value[startIndex++] << 8)) | value[startIndex++]);

            //fixed (byte* numRef = &(value[startIndex]))
            //{
            //    return ((((numRef[0] << 0x18) | (numRef[1] << 0x10)) | (numRef[2] << 8)) | numRef[3]);
            //}
        }

        private static DateTime TZif_UnixTimeToWindowsTime(int unixTime)
        {
            long fileTime = (unixTime + 0x2b6109100L) * 0x989680L;
            return DateTime.FromFileTimeUtc(fileTime);
        }




        private TimeZoneInfo(byte[] data, bool dstDisabled)
        {
            TZifHead head;
            DateTime[] timeArray;
            byte[] buffer;
            TZifType[] typeArray;
            string str;
            bool[] flagArray;
            bool[] flagArray2;
            TZif_ParseRaw(data, out head, out timeArray, out buffer, out typeArray, out str, out flagArray, out flagArray2);
            this.m_id = "Local";
            this.m_displayName = "Local";
            this.m_baseUtcOffset = TimeSpan.Zero;
            DateTime utcNow = DateTime.UtcNow;
            for (int i = 0; (i < timeArray.Length) && (timeArray[i] <= utcNow); i++)
            {
                int index = buffer[i];
                if (!typeArray[index].IsDst)
                {
                    this.m_baseUtcOffset = typeArray[index].UtcOffset;
                    this.m_standardDisplayName = TZif_GetZoneAbbreviation(str, typeArray[index].AbbreviationIndex);
                }
                else
                {
                    this.m_daylightDisplayName = TZif_GetZoneAbbreviation(str, typeArray[index].AbbreviationIndex);
                }
            }
            if (timeArray.Length == 0)
            {
                for (int j = 0; j < typeArray.Length; j++)
                {
                    if (!typeArray[j].IsDst)
                    {
                        this.m_baseUtcOffset = typeArray[j].UtcOffset;
                        this.m_standardDisplayName = TZif_GetZoneAbbreviation(str, typeArray[j].AbbreviationIndex);
                    }
                    else
                    {
                        this.m_daylightDisplayName = TZif_GetZoneAbbreviation(str, typeArray[j].AbbreviationIndex);
                    }
                }
            }
            this.m_id = this.m_standardDisplayName;
            this.m_displayName = this.m_standardDisplayName;
            if ((this.m_baseUtcOffset.Ticks % 0x23c34600L) != 0L)
            {
                this.m_baseUtcOffset = new TimeSpan(this.m_baseUtcOffset.Hours, this.m_baseUtcOffset.Minutes, 0);
            }
            if (!dstDisabled)
            {
                TZif_GenerateAdjustmentRules(out this.m_adjustmentRules, timeArray, buffer, typeArray, flagArray, flagArray2);
            }
            ValidateTimeZoneInfo(this.m_id, this.m_baseUtcOffset, this.m_adjustmentRules, out this.m_supportsDaylightSavingTime);
        }





        private static TimeZoneInfo GetTimeZone(string id)
        {
            throw new NotImplementedException();
            //TimeZoneInfo info;
            //Exception exception;
            //if (id == null)
            //{
            //    throw new ArgumentNullException("id");
            //}
            //if (((id.Length == 0) || (id.Length > 0xff)) || id.Contains("\0"))
            //{
            //    throw new TimeZoneNotFoundException(SR.GetString("TimeZoneNotFound_MissingRegistryData", new object[] { id }));
            //}
            //switch (TryGetTimeZone(id, false, out info, out exception))
            //{
            //    case TimeZoneInfoResult.Success:
            //        return info;

            //    case TimeZoneInfoResult.InvalidTimeZoneException:
            //        throw new InvalidTimeZoneException(SR.GetString("InvalidTimeZone_InvalidRegistryData", new object[] { id }), exception);

            //    case TimeZoneInfoResult.SecurityException:
            //        throw new SecurityException(SR.GetString("Security_CannotReadRegistryData", new object[] { id }), exception);
            //}
            //throw new TimeZoneNotFoundException(SR.GetString("TimeZoneNotFound_MissingRegistryData", new object[] { id }), exception);
        }

        public TimeSpan GetUtcOffset(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Local)
            {
                DateTime time;
                lock (s_internalSyncObject)
                {
                    if (this.GetCorrespondingKind() == DateTimeKind.Local)
                    {
                        return GetUtcOffset(dateTime, this);
                    }
                    time = ConvertTime(dateTime, Local, Utc, TimeZoneInfoOptions.NoThrowOnInvalidTime);
                }
                return GetUtcOffsetFromUtc(time, this);
            }
            if (dateTime.Kind != DateTimeKind.Utc)
            {
                return GetUtcOffset(dateTime, this);
            }
            if (this.GetCorrespondingKind() == DateTimeKind.Utc)
            {
                return this.m_baseUtcOffset;
            }
            return GetUtcOffsetFromUtc(dateTime, this);
        }

        public TimeSpan GetUtcOffset(DateTimeOffset dateTimeOffset)
        {
            return GetUtcOffsetFromUtc(dateTimeOffset.UtcDateTime, this);
        }

        private static TimeSpan GetUtcOffset(DateTime time, TimeZoneInfo zone)
        {
            TimeSpan baseUtcOffset = zone.BaseUtcOffset;
            AdjustmentRule adjustmentRuleForTime = zone.GetAdjustmentRuleForTime(time);
            if (adjustmentRuleForTime != null)
            {
                DaylightTime daylightTime = GetDaylightTime(time.Year, adjustmentRuleForTime);
                bool flag = GetIsDaylightSavings(time, adjustmentRuleForTime, daylightTime);
                baseUtcOffset += flag ? adjustmentRuleForTime.DaylightDelta : TimeSpan.Zero;
            }
            return baseUtcOffset;
        }

        private static TimeSpan GetUtcOffsetFromUtc(DateTime time, TimeZoneInfo zone)
        {
            bool flag;
            return GetUtcOffsetFromUtc(time, zone, out flag);
        }

        private static TimeSpan GetUtcOffsetFromUtc(DateTime time, TimeZoneInfo zone, out bool isDaylightSavings)
        {
            int year;
            AdjustmentRule adjustmentRuleForTime;
            isDaylightSavings = false;
            TimeSpan baseUtcOffset = zone.BaseUtcOffset;
            if (time > new DateTime(0x270f, 12, 0x1f))
            {
                adjustmentRuleForTime = zone.GetAdjustmentRuleForTime(DateTime.MaxValue);
                year = 0x270f;
            }
            else if (time < new DateTime(1, 1, 2))
            {
                adjustmentRuleForTime = zone.GetAdjustmentRuleForTime(DateTime.MinValue);
                year = 1;
            }
            else
            {
                DateTime dateTime = time + baseUtcOffset;
                year = time.Year;
                adjustmentRuleForTime = zone.GetAdjustmentRuleForTime(dateTime);
            }
            if (adjustmentRuleForTime != null)
            {
                isDaylightSavings = GetIsDaylightSavingsFromUtc(time, year, zone.m_baseUtcOffset, adjustmentRuleForTime);
                baseUtcOffset += isDaylightSavings ? adjustmentRuleForTime.DaylightDelta : TimeSpan.Zero;
            }
            return baseUtcOffset;
        }

        public bool HasSameRules(TimeZoneInfo other)
        {
            if (other == null)
            {
                throw new ArgumentNullException("other");
            }
            if ((this.m_baseUtcOffset != other.m_baseUtcOffset) || (this.m_supportsDaylightSavingTime != other.m_supportsDaylightSavingTime))
            {
                return false;
            }
            AdjustmentRule[] adjustmentRules = this.m_adjustmentRules;
            AdjustmentRule[] ruleArray2 = other.m_adjustmentRules;
            bool flag = ((adjustmentRules == null) && (ruleArray2 == null)) || ((adjustmentRules != null) && (ruleArray2 != null));
            if (!flag)
            {
                return false;
            }
            if (adjustmentRules != null)
            {
                if (adjustmentRules.Length != ruleArray2.Length)
                {
                    return false;
                }
                for (int i = 0; i < adjustmentRules.Length; i++)
                {
                    if (!adjustmentRules[i].Equals(ruleArray2[i]))
                    {
                        return false;
                    }
                }
            }
            return flag;
        }

        public bool IsAmbiguousTime(DateTime dateTime)
        {
            DateTime time;
            AdjustmentRule rule;
            if (!this.m_supportsDaylightSavingTime)
            {
                return false;
            }
            if (dateTime.Kind == DateTimeKind.Local)
            {
                lock (s_internalSyncObject)
                {
                    time = ConvertTime(dateTime, Local, this, TimeZoneInfoOptions.NoThrowOnInvalidTime);
                    goto Label_0068;
                }
            }
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                lock (s_internalSyncObject)
                {
                    time = ConvertTime(dateTime, Utc, this, TimeZoneInfoOptions.NoThrowOnInvalidTime);
                    goto Label_0068;
                }
            }
            time = dateTime;
        Label_0068:
            rule = this.GetAdjustmentRuleForTime(time);
            if (rule != null)
            {
                DaylightTime daylightTime = GetDaylightTime(time.Year, rule);
                return GetIsAmbiguousTime(time, rule, daylightTime);
            }
            return false;
        }

        public bool IsAmbiguousTime(DateTimeOffset dateTimeOffset)
        {
            if (!this.m_supportsDaylightSavingTime)
            {
                return false;
            }
            DateTimeOffset offset = ConvertTime(dateTimeOffset, this);
            return this.IsAmbiguousTime(offset.DateTime);
        }

        public bool IsDaylightSavingTime(DateTime dateTime)
        {
            DateTime time;
            AdjustmentRule rule;
            if (!this.m_supportsDaylightSavingTime || (this.m_adjustmentRules == null))
            {
                return false;
            }
            if (dateTime.Kind == DateTimeKind.Local)
            {
                lock (s_internalSyncObject)
                {
                    time = ConvertTime(dateTime, Local, this, TimeZoneInfoOptions.NoThrowOnInvalidTime);
                    goto Label_0064;
                }
            }
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                bool flag;
                if (this.GetCorrespondingKind() == DateTimeKind.Utc)
                {
                    return false;
                }
                GetUtcOffsetFromUtc(dateTime, this, out flag);
                return flag;
            }
            time = dateTime;
        Label_0064:
            rule = this.GetAdjustmentRuleForTime(time);
            if (rule != null)
            {
                DaylightTime daylightTime = GetDaylightTime(time.Year, rule);
                return GetIsDaylightSavings(time, rule, daylightTime);
            }
            return false;
        }

        public bool IsDaylightSavingTime(DateTimeOffset dateTimeOffset)
        {
            bool flag;
            GetUtcOffsetFromUtc(dateTimeOffset.UtcDateTime, this, out flag);
            return flag;
        }

        public bool IsInvalidTime(DateTime dateTime)
        {
            bool flag = false;
            if ((dateTime.Kind != DateTimeKind.Unspecified) && ((dateTime.Kind != DateTimeKind.Local) || (this.GetCorrespondingKind() != DateTimeKind.Local)))
            {
                return flag;
            }
            AdjustmentRule adjustmentRuleForTime = this.GetAdjustmentRuleForTime(dateTime);
            if (adjustmentRuleForTime != null)
            {
                DaylightTime daylightTime = GetDaylightTime(dateTime.Year, adjustmentRuleForTime);
                return GetIsInvalidTime(dateTime, adjustmentRuleForTime, daylightTime);
            }
            return false;
        }

        void OnDeserialization(object sender)
        {
            try
            {
                bool flag;
                ValidateTimeZoneInfo(this.m_id, this.m_baseUtcOffset, this.m_adjustmentRules, out flag);
                if (flag != this.m_supportsDaylightSavingTime)
                {
                    //throw new SerializationException(SR.GetString("Serialization_CorruptField", new object[] { "SupportsDaylightSavingTime" }));
                }
            }
            catch{}
        }


        public string ToSerializedString()
        {
            return StringSerializer.GetSerializedString(this);
        }

        public override string ToString()
        {
            return this.DisplayName;
        }


        private static DateTime TransitionTimeToDateTime(int year, TransitionTime transitionTime)
        {
            DateTime time;
            DateTime timeOfDay = transitionTime.TimeOfDay;
            if (transitionTime.IsFixedDateRule)
            {
                int num = DateTime.DaysInMonth(year, transitionTime.Month);
                return new DateTime(year, transitionTime.Month, (num < transitionTime.Day) ? num : transitionTime.Day, timeOfDay.Hour, timeOfDay.Minute, timeOfDay.Second, timeOfDay.Millisecond);
            }
            if (transitionTime.Week <= 4)
            {
                time = new DateTime(year, transitionTime.Month, 1, timeOfDay.Hour, timeOfDay.Minute, timeOfDay.Second, timeOfDay.Millisecond);
                int dayOfWeek = (int)time.DayOfWeek;
                int num3 = ((int)transitionTime.DayOfWeek) - dayOfWeek;
                if (num3 < 0)
                {
                    num3 += 7;
                }
                num3 += 7 * (transitionTime.Week - 1);
                if (num3 > 0)
                {
                    time = time.AddDays((double)num3);
                }
                return time;
            }
            int day = DateTime.DaysInMonth(year, transitionTime.Month);
            time = new DateTime(year, transitionTime.Month, day, timeOfDay.Hour, timeOfDay.Minute, timeOfDay.Second, timeOfDay.Millisecond);
            int num6 = (int)(time.DayOfWeek - transitionTime.DayOfWeek);
            if (num6 < 0)
            {
                num6 += 7;
            }
            if (num6 > 0)
            {
                time = time.AddDays((double)-num6);
            }
            return time;
        }



        private static bool TryCreateAdjustmentRules(string id, object defaultTimeZoneInformation, out AdjustmentRule[] rules, out Exception e)
        {

            //NativeMethods.RegistryTimeZoneInformation
            throw new NotImplementedException();
            //e = null;
            //try
            //{
            //    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(string.Format(CultureInfo.InvariantCulture, @"{0}\{1}\Dynamic DST", new object[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones", id }), RegistryKeyPermissionCheck.Default, RegistryRights.ExecuteKey))
            //    {
            //        if (key == null)
            //        {
            //            AdjustmentRule rule = CreateAdjustmentRuleFromTimeZoneInformation(defaultTimeZoneInformation, DateTime.MinValue.Date, DateTime.MaxValue.Date);
            //            if (rule == null)
            //            {
            //                rules = null;
            //            }
            //            else
            //            {
            //                rules = new AdjustmentRule[] { rule };
            //            }
            //            return true;
            //        }
            //        int year = (int)key.GetValue("FirstEntry", -1, RegistryValueOptions.None);
            //        int num2 = (int)key.GetValue("LastEntry", -1, RegistryValueOptions.None);
            //        if (((year == -1) || (num2 == -1)) || (year > num2))
            //        {
            //            rules = null;
            //            return false;
            //        }
            //        NativeMethods.RegistryTimeZoneInformation timeZoneInformation = new NativeMethods.RegistryTimeZoneInformation((byte[])key.GetValue(year.ToString(CultureInfo.InvariantCulture), null, RegistryValueOptions.None));
            //        if (year == num2)
            //        {
            //            AdjustmentRule rule2 = CreateAdjustmentRuleFromTimeZoneInformation(timeZoneInformation, DateTime.MinValue.Date, DateTime.MaxValue.Date);
            //            if (rule2 == null)
            //            {
            //                rules = null;
            //            }
            //            else
            //            {
            //                rules = new AdjustmentRule[] { rule2 };
            //            }
            //            return true;
            //        }
            //        List<AdjustmentRule> list = new List<AdjustmentRule>(1);
            //        AdjustmentRule item = CreateAdjustmentRuleFromTimeZoneInformation(timeZoneInformation, DateTime.MinValue.Date, new DateTime(year, 12, 0x1f));
            //        if (item != null)
            //        {
            //            list.Add(item);
            //        }
            //        for (int i = year + 1; i < num2; i++)
            //        {
            //            timeZoneInformation = new NativeMethods.RegistryTimeZoneInformation((byte[])key.GetValue(i.ToString(CultureInfo.InvariantCulture), null, RegistryValueOptions.None));
            //            AdjustmentRule rule4 = CreateAdjustmentRuleFromTimeZoneInformation(timeZoneInformation, new DateTime(i, 1, 1), new DateTime(i, 12, 0x1f));
            //            if (rule4 != null)
            //            {
            //                list.Add(rule4);
            //            }
            //        }
            //        timeZoneInformation = new NativeMethods.RegistryTimeZoneInformation((byte[])key.GetValue(num2.ToString(CultureInfo.InvariantCulture), null, RegistryValueOptions.None));
            //        AdjustmentRule rule5 = CreateAdjustmentRuleFromTimeZoneInformation(timeZoneInformation, new DateTime(num2, 1, 1), DateTime.MaxValue.Date);
            //        if (rule5 != null)
            //        {
            //            list.Add(rule5);
            //        }
            //        rules = list.ToArray();
            //        if ((rules != null) && (rules.Length == 0))
            //        {
            //            rules = null;
            //        }
            //    }
            //}
            //catch (InvalidCastException exception)
            //{
            //    rules = null;
            //    e = exception;
            //    return false;
            //}
            //catch (ArgumentOutOfRangeException exception2)
            //{
            //    rules = null;
            //    e = exception2;
            //    return false;
            //}
            //catch (ArgumentException exception3)
            //{
            //    rules = null;
            //    e = exception3;
            //    return false;
            //}
            //return true;
        }

        private static string TryGetLocalizedNameByMuiNativeResource(string resource)
        {
            throw new NotImplementedException();
            //string str;
            //int num;
            //if (string.IsNullOrEmpty(resource))
            //{
            //    return string.Empty;
            //}
            //string[] strArray = resource.Split(new char[] { ',' }, StringSplitOptions.None);
            //if (strArray.Length != 2)
            //{
            //    return string.Empty;
            //}
            //string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            //string str3 = strArray[0].TrimStart(new char[] { '@' });
            //try
            //{
            //    str = Path.Combine(folderPath, str3);
            //}
            //catch (ArgumentException)
            //{
            //    return string.Empty;
            //}
            //if (!int.TryParse(strArray[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out num))
            //{
            //    return string.Empty;
            //}
            //num = -num;
            //try
            //{
            //    StringBuilder fileMuiPath = new StringBuilder(260)
            //    {
            //        Length = 260
            //    };
            //    int fileMuiPathLength = 260;
            //    int languageLength = 0;
            //    long enumerator = 0L;
            //    if (!UnsafeNativeMethods.GetFileMUIPath(0x10, str, null, ref languageLength, fileMuiPath, ref fileMuiPathLength, ref enumerator))
            //    {
            //        return string.Empty;
            //    }
            //    return TryGetLocalizedNameByNativeResource(fileMuiPath.ToString(), num);
            //}
            //catch (EntryPointNotFoundException)
            //{
            //    return string.Empty;
            //}
        }

        [SecurityCritical]
        private static string TryGetLocalizedNameByNativeResource(string filePath, int resource)
        {
            throw new NotImplementedException();
            //if (File.Exists(filePath))
            //{
            //    using (SafeLibraryHandle handle = UnsafeNativeMethods.LoadLibraryEx(filePath, IntPtr.Zero, 2))
            //    {
            //        if (!handle.IsInvalid)
            //        {
            //            StringBuilder buffer = new StringBuilder(500)
            //            {
            //                Length = 500
            //            };
            //            if (UnsafeNativeMethods.LoadString(handle, resource, buffer, buffer.Length) != 0)
            //            {
            //                return buffer.ToString();
            //            }
            //        }
            //    }
            //}
            //return string.Empty;
        }

        private static bool TryGetLocalizedNamesByRegistryKey(object key, out string displayName, out string standardName, out string daylightName)
        {
            throw new NotImplementedException();

            //displayName = string.Empty;
            //standardName = string.Empty;
            //daylightName = string.Empty;
            //string str = key.GetValue("MUI_Display", string.Empty, RegistryValueOptions.None) as string;
            //string str2 = key.GetValue("MUI_Std", string.Empty, RegistryValueOptions.None) as string;
            //string str3 = key.GetValue("MUI_Dlt", string.Empty, RegistryValueOptions.None) as string;
            //if (!string.IsNullOrEmpty(str))
            //{
            //    displayName = TryGetLocalizedNameByMuiNativeResource(str);
            //}
            //if (!string.IsNullOrEmpty(str2))
            //{
            //    standardName = TryGetLocalizedNameByMuiNativeResource(str2);
            //}
            //if (!string.IsNullOrEmpty(str3))
            //{
            //    daylightName = TryGetLocalizedNameByMuiNativeResource(str3);
            //}
            //if (string.IsNullOrEmpty(displayName))
            //{
            //    displayName = key.GetValue("Display", string.Empty, RegistryValueOptions.None) as string;
            //}
            //if (string.IsNullOrEmpty(standardName))
            //{
            //    standardName = key.GetValue("Std", string.Empty, RegistryValueOptions.None) as string;
            //}
            //if (string.IsNullOrEmpty(daylightName))
            //{
            //    daylightName = key.GetValue("Dlt", string.Empty, RegistryValueOptions.None) as string;
            //}
            //return true;
        }

        private static TimeZoneInfoResult TryGetTimeZone(string id, bool dstDisabled, out TimeZoneInfo value, out Exception e)
        {
            TimeZoneInfoResult success = TimeZoneInfoResult.Success;
            e = null;
            TimeZoneInfo info = null;
            if (s_systemTimeZones.TryGetValue(id, out info))
            {
                if (dstDisabled && info.m_supportsDaylightSavingTime)
                {
                    value = CreateCustomTimeZone(info.m_id, info.m_baseUtcOffset, info.m_displayName, info.m_standardDisplayName);
                    return success;
                }
                value = new TimeZoneInfo(info.m_id, info.m_baseUtcOffset, info.m_displayName, info.m_standardDisplayName, info.m_daylightDisplayName, info.m_adjustmentRules, false);
                return success;
            }
            if (!s_allSystemTimeZonesRead)
            {
                success = TryGetTimeZoneByRegistryKey(id, out info, out e);
                if (success == TimeZoneInfoResult.Success)
                {
                    s_systemTimeZones.Add(id, info);
                    if (dstDisabled && info.m_supportsDaylightSavingTime)
                    {
                        value = CreateCustomTimeZone(info.m_id, info.m_baseUtcOffset, info.m_displayName, info.m_standardDisplayName);
                        return success;
                    }
                    value = new TimeZoneInfo(info.m_id, info.m_baseUtcOffset, info.m_displayName, info.m_standardDisplayName, info.m_daylightDisplayName, info.m_adjustmentRules, false);
                    return success;
                }
                value = null;
                return success;
            }
            success = TimeZoneInfoResult.TimeZoneNotFoundException;
            value = null;
            return success;
        }

        private static TimeZoneInfoResult TryGetTimeZoneByRegistryKey(string id, out TimeZoneInfo value, out Exception e)
        {
            e = null;
            value = null;
            return TimeZoneInfoResult.TimeZoneNotFoundException;
            throw new NotImplementedException();

            //TimeZoneInfoResult invalidTimeZoneException;
            //e = null;
            //try
            //{
            //    PermissionSet set = new PermissionSet(PermissionState.None);
            //    set.AddPermission(new RegistryPermission(RegistryPermissionAccess.Read, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones"));
            //    set.Assert();
            //    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(string.Format(CultureInfo.InvariantCulture, @"{0}\{1}", new object[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones", id }), RegistryKeyPermissionCheck.Default, RegistryRights.ExecuteKey))
            //    {
            //        NativeMethods.RegistryTimeZoneInformation information;
            //        AdjustmentRule[] ruleArray;
            //        string str;
            //        string str2;
            //        string str3;
            //        if (key == null)
            //        {
            //            value = null;
            //            return TimeZoneInfoResult.TimeZoneNotFoundException;
            //        }
            //        try
            //        {
            //            information = new NativeMethods.RegistryTimeZoneInformation((byte[])key.GetValue("TZI", null, RegistryValueOptions.None));
            //        }
            //        catch (InvalidCastException exception)
            //        {
            //            value = null;
            //            e = exception;
            //            return TimeZoneInfoResult.InvalidTimeZoneException;
            //        }
            //        catch (ArgumentException exception2)
            //        {
            //            value = null;
            //            e = exception2;
            //            return TimeZoneInfoResult.InvalidTimeZoneException;
            //        }
            //        if (!TryCreateAdjustmentRules(id, information, out ruleArray, out e))
            //        {
            //            value = null;
            //            return TimeZoneInfoResult.InvalidTimeZoneException;
            //        }
            //        if (!TryGetLocalizedNamesByRegistryKey(key, out str, out str2, out str3))
            //        {
            //            value = null;
            //            invalidTimeZoneException = TimeZoneInfoResult.InvalidTimeZoneException;
            //        }
            //        else
            //        {
            //            try
            //            {
            //                value = new TimeZoneInfo(id, new TimeSpan(0, -information.Bias, 0), str, str2, str3, ruleArray, false);
            //                invalidTimeZoneException = TimeZoneInfoResult.Success;
            //            }
            //            catch (ArgumentException exception3)
            //            {
            //                value = null;
            //                e = exception3;
            //                invalidTimeZoneException = TimeZoneInfoResult.InvalidTimeZoneException;
            //            }
            //            catch (InvalidTimeZoneException exception4)
            //            {
            //                value = null;
            //                e = exception4;
            //                invalidTimeZoneException = TimeZoneInfoResult.InvalidTimeZoneException;
            //            }
            //        }
            //    }
            //}
            //finally
            //{
            //    PermissionSet.RevertAssert();
            //}
            //return invalidTimeZoneException;
        }

        internal static bool UtcOffsetOutOfRange(TimeSpan offset)
        {
            if (offset.TotalHours >= -14.0)
            {
                return (offset.TotalHours > 14.0);
            }
            return true;
        }

        private static void ValidateTimeZoneInfo(string id, TimeSpan baseUtcOffset, AdjustmentRule[] adjustmentRules, out bool adjustmentRulesSupportDst)
        {
            adjustmentRulesSupportDst = false;
            if (id == null)
            {
                throw new ArgumentNullException("id");
            }
            if (id.Length == 0)
            {
                //throw new ArgumentException(SR.GetString("Argument_InvalidId", new object[] { id }), "id");
            }
            if (UtcOffsetOutOfRange(baseUtcOffset))
            {
                // throw new ArgumentOutOfRangeException("baseUtcOffset", SR.GetString("ArgumentOutOfRange_UtcOffset"));
            }
            if ((baseUtcOffset.Ticks % 0x23c34600L) != 0L)
            {
                //throw new ArgumentException(SR.GetString("Argument_TimeSpanHasSeconds"), "baseUtcOffset");
            }
            if ((adjustmentRules != null) && (adjustmentRules.Length != 0))
            {
                adjustmentRulesSupportDst = true;
                AdjustmentRule rule = null;
                AdjustmentRule rule2 = null;
                for (int i = 0; i < adjustmentRules.Length; i++)
                {
                    rule = rule2;
                    rule2 = adjustmentRules[i];
                    if (rule2 == null)
                    {
                        // throw new InvalidTimeZoneException(SR.GetString("Argument_AdjustmentRulesNoNulls"));
                    }
                    if (UtcOffsetOutOfRange(baseUtcOffset + rule2.DaylightDelta))
                    {
                        // throw new InvalidTimeZoneException(SR.GetString("ArgumentOutOfRange_UtcOffsetAndDaylightDelta"));
                    }
                    if ((rule != null) && (rule2.DateStart <= rule.DateEnd))
                    {
                        // throw new InvalidTimeZoneException(SR.GetString("Argument_AdjustmentRulesOutOfOrder"));
                    }
                }
            }
        }

        // Properties
        public TimeSpan BaseUtcOffset
        {
            get
            {
                return this.m_baseUtcOffset;
            }
        }

        public string DaylightName
        {
            get
            {
                if (this.m_daylightDisplayName != null)
                {
                    return this.m_daylightDisplayName;
                }
                return string.Empty;
            }
        }

        public string DisplayName
        {
            get
            {
                if (this.m_displayName != null)
                {
                    return this.m_displayName;
                }
                return string.Empty;
            }
        }

        public string Id
        {
            get
            {
                return this.m_id;
            }
        }

        public static TimeZoneInfo Local
        {
            [SecurityCritical]
            get
            {
                TimeZoneInfo info = s_localTimeZone;
                if (info != null)
                {
                    return info;
                }
                lock (s_internalSyncObject)
                {
                    if (s_localTimeZone == null)
                    {
                        throw new NotImplementedException("TimeZoneINfo.Local cant be used unless you set the Local Timezone with this: SetLocalTimeZoneBySerializedString(string serializedString)");
                        //s_localTimeZone = new TimeZoneInfo(localTimeZone.m_id, localTimeZone.m_baseUtcOffset, localTimeZone.m_displayName, localTimeZone.m_standardDisplayName, localTimeZone.m_daylightDisplayName, localTimeZone.m_adjustmentRules, false);
                    }
                    return s_localTimeZone;
                }
            }
        }

        private static object s_internalSyncObject
        {
            get
            {
                if (s_hiddenInternalSyncObject == null)
                {
                    object obj2 = new object();
                    Interlocked.CompareExchange(ref s_hiddenInternalSyncObject, obj2, null);
                }
                return s_hiddenInternalSyncObject;
            }
        }

        private static Dictionary<string, TimeZoneInfo> s_systemTimeZones
        {
            get
            {
                if (s_hiddenSystemTimeZones == null)
                {
                    s_hiddenSystemTimeZones = new Dictionary<string, TimeZoneInfo>();
                }
                return s_hiddenSystemTimeZones;
            }
            set
            {
                s_hiddenSystemTimeZones = value;
            }
        }

        public string StandardName
        {
            get
            {
                if (this.m_standardDisplayName != null)
                {
                    return this.m_standardDisplayName;
                }
                return string.Empty;
            }
        }

        public bool SupportsDaylightSavingTime
        {
            get
            {
                return this.m_supportsDaylightSavingTime;
            }
        }

        public static TimeZoneInfo Utc
        {
            get
            {
                TimeZoneInfo info = s_utcTimeZone;
                if (info != null)
                {
                    return info;
                }
                lock (s_internalSyncObject)
                {
                    if (s_utcTimeZone == null)
                    {
                        s_utcTimeZone = CreateCustomTimeZone("UTC", TimeSpan.Zero, "UTC", "UTC");
                    }
                    return s_utcTimeZone;
                }
            }
        }

        // Nested Types
        public sealed class AdjustmentRule : IEquatable<TimeZoneInfo.AdjustmentRule>
        {
            // Fields
            private DateTime m_dateEnd;
            private DateTime m_dateStart;
            private TimeSpan m_daylightDelta;
            private TimeZoneInfo.TransitionTime m_daylightTransitionEnd;
            private TimeZoneInfo.TransitionTime m_daylightTransitionStart;

            // Methods
            private AdjustmentRule()
            {
            }

            private AdjustmentRule(object info, StreamingContext context)
            {

                throw new NotImplementedException();
                //if (info == null)
                //{
                //    throw new ArgumentNullException("info");
                //}
                //this.m_dateStart = (DateTime)info.GetValue("DateStart", typeof(DateTime));
                //this.m_dateEnd = (DateTime)info.GetValue("DateEnd", typeof(DateTime));
                //this.m_daylightDelta = (TimeSpan)info.GetValue("DaylightDelta", typeof(TimeSpan));
                //this.m_daylightTransitionStart = (TimeZoneInfo.TransitionTime)info.GetValue("DaylightTransitionStart", typeof(TimeZoneInfo.TransitionTime));
                //this.m_daylightTransitionEnd = (TimeZoneInfo.TransitionTime)info.GetValue("DaylightTransitionEnd", typeof(TimeZoneInfo.TransitionTime));
            }

            public static TimeZoneInfo.AdjustmentRule CreateAdjustmentRule(DateTime dateStart, DateTime dateEnd, TimeSpan daylightDelta, TimeZoneInfo.TransitionTime daylightTransitionStart, TimeZoneInfo.TransitionTime daylightTransitionEnd)
            {
                ValidateAdjustmentRule(dateStart, dateEnd, daylightDelta, daylightTransitionStart, daylightTransitionEnd);
                return new TimeZoneInfo.AdjustmentRule { m_dateStart = dateStart, m_dateEnd = dateEnd, m_daylightDelta = daylightDelta, m_daylightTransitionStart = daylightTransitionStart, m_daylightTransitionEnd = daylightTransitionEnd };
            }

            public bool Equals(TimeZoneInfo.AdjustmentRule other)
            {
                return ((((((other != null) && (this.m_dateStart == other.m_dateStart)) && (this.m_dateEnd == other.m_dateEnd)) && (this.m_daylightDelta == other.m_daylightDelta)) && this.m_daylightTransitionEnd.Equals(other.m_daylightTransitionEnd)) && this.m_daylightTransitionStart.Equals(other.m_daylightTransitionStart));
            }

            public override int GetHashCode()
            {
                return this.m_dateStart.GetHashCode();
            }

            void OnDeserialization(object sender)
            {
                try
                {
                    ValidateAdjustmentRule(this.m_dateStart, this.m_dateEnd, this.m_daylightDelta, this.m_daylightTransitionStart, this.m_daylightTransitionEnd);
                }
                catch
                {
                    //throw new SerializationException(SR.GetString("Serialization_InvalidData"), exception);
                }
            }


            private static void ValidateAdjustmentRule(DateTime dateStart, DateTime dateEnd, TimeSpan daylightDelta, TimeZoneInfo.TransitionTime daylightTransitionStart, TimeZoneInfo.TransitionTime daylightTransitionEnd)
            {
                if (dateStart.Kind != DateTimeKind.Unspecified)
                {
                    //throw new ArgumentException(SR.GetString("Argument_DateTimeKindMustBeUnspecified"), "dateStart");
                }
                if (dateEnd.Kind != DateTimeKind.Unspecified)
                {
                    //throw new ArgumentException(SR.GetString("Argument_DateTimeKindMustBeUnspecified"), "dateEnd");
                }
                if (daylightTransitionStart.Equals(daylightTransitionEnd))
                {
                    //throw new ArgumentException(SR.GetString("Argument_TransitionTimesAreIdentical"), "daylightTransitionEnd");
                }
                if (dateStart > dateEnd)
                {
                    //throw new ArgumentException(SR.GetString("Argument_OutOfOrderDateTimes"), "dateStart");
                }
                if (TimeZoneInfo.UtcOffsetOutOfRange(daylightDelta))
                {
                    //throw new ArgumentOutOfRangeException("daylightDelta", daylightDelta, SR.GetString("ArgumentOutOfRange_UtcOffset"));
                }
                if ((daylightDelta.Ticks % 0x23c34600L) != 0L)
                {
                    //throw new ArgumentException(SR.GetString("Argument_TimeSpanHasSeconds"), "daylightDelta");
                }
                if (dateStart.TimeOfDay != TimeSpan.Zero)
                {
                    //throw new ArgumentException(SR.GetString("Argument_DateTimeHasTimeOfDay"), "dateStart");
                }
                if (dateEnd.TimeOfDay != TimeSpan.Zero)
                {
                    //throw new ArgumentException(SR.GetString("Argument_DateTimeHasTimeOfDay"), "dateEnd");
                }
            }

            // Properties
            public DateTime DateEnd
            {
                get
                {
                    return this.m_dateEnd;
                }
            }

            public DateTime DateStart
            {
                get
                {
                    return this.m_dateStart;
                }
            }

            public TimeSpan DaylightDelta
            {
                get
                {
                    return this.m_daylightDelta;
                }
            }

            public TimeZoneInfo.TransitionTime DaylightTransitionEnd
            {
                get
                {
                    return this.m_daylightTransitionEnd;
                }
            }

            public TimeZoneInfo.TransitionTime DaylightTransitionStart
            {
                get
                {
                    return this.m_daylightTransitionStart;
                }
            }
        }

        private sealed class StringSerializer
        {
            // Fields
            private const string dateTimeFormat = "MM:dd:yyyy";
            private const char esc = '\\';
            private const string escapedEsc = @"\\";
            private const string escapedLhs = @"\[";
            private const string escapedRhs = @"\]";
            private const string escapedSep = @"\;";
            private const string escString = @"\";
            private const int initialCapacityForString = 0x40;
            private const char lhs = '[';
            private const string lhsString = "[";
            private int m_currentTokenStartIndex;
            private string m_serializedText;
            private State m_state;
            private const char rhs = ']';
            private const string rhsString = "]";
            private const char sep = ';';
            private const string sepString = ";";
            private const string timeOfDayFormat = "HH:mm:ss.FFF";

            // Methods
            private StringSerializer(string str)
            {
                this.m_serializedText = str;
                this.m_state = State.StartOfToken;
            }

            public static TimeZoneInfo GetDeserializedTimeZoneInfo(string source)
            {
                TimeZoneInfo info;
                TimeZoneInfo.StringSerializer serializer = new TimeZoneInfo.StringSerializer(source);
                string nextStringValue = serializer.GetNextStringValue(false);
                TimeSpan nextTimeSpanValue = serializer.GetNextTimeSpanValue(false);
                string displayName = serializer.GetNextStringValue(false);
                string standardDisplayName = serializer.GetNextStringValue(false);
                string daylightDisplayName = serializer.GetNextStringValue(false);
                TimeZoneInfo.AdjustmentRule[] nextAdjustmentRuleArrayValue = serializer.GetNextAdjustmentRuleArrayValue(false);
                //try
                //{
                info = TimeZoneInfo.CreateCustomTimeZone(nextStringValue, nextTimeSpanValue, displayName, standardDisplayName, daylightDisplayName, nextAdjustmentRuleArrayValue);
                //}
                //catch (ArgumentException exception)
                //{

                //    //throw new SerializationException(SR.GetString("Serialization_InvalidData"), exception);
                //}
                //catch (InvalidTimeZoneException exception2)
                //{
                //    //throw new SerializationException(SR.GetString("Serialization_InvalidData"), exception2);
                //}
                return info;
            }

            private TimeZoneInfo.AdjustmentRule[] GetNextAdjustmentRuleArrayValue(bool canEndWithoutSeparator)
            {
                List<TimeZoneInfo.AdjustmentRule> list = new List<TimeZoneInfo.AdjustmentRule>(1);
                int num = 0;
                for (TimeZoneInfo.AdjustmentRule rule = this.GetNextAdjustmentRuleValue(true); rule != null; rule = this.GetNextAdjustmentRuleValue(true))
                {
                    list.Add(rule);
                    num++;
                }
                if (!canEndWithoutSeparator)
                {
                    if (this.m_state == State.EndOfLine)
                    {
                        //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                    }
                    if ((this.m_currentTokenStartIndex < 0) || (this.m_currentTokenStartIndex >= this.m_serializedText.Length))
                    {
                        //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                    }
                }
                if (num == 0)
                {
                    return null;
                }
                return list.ToArray();
            }

            private TimeZoneInfo.AdjustmentRule GetNextAdjustmentRuleValue(bool canEndWithoutSeparator)
            {
                TimeZoneInfo.AdjustmentRule rule = null;
                if (this.m_state == State.EndOfLine)
                {
                    if (!canEndWithoutSeparator)
                    {
                        //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                    }
                    return null;
                }
                if ((this.m_currentTokenStartIndex < 0) || (this.m_currentTokenStartIndex >= this.m_serializedText.Length))
                {
                    //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                if (this.m_serializedText[this.m_currentTokenStartIndex] == ';')
                {
                    return null;
                }
                if (this.m_serializedText[this.m_currentTokenStartIndex] != '[')
                {
                    // //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                this.m_currentTokenStartIndex++;
                DateTime nextDateTimeValue = this.GetNextDateTimeValue(false, "MM:dd:yyyy");
                DateTime dateEnd = this.GetNextDateTimeValue(false, "MM:dd:yyyy");
                TimeSpan nextTimeSpanValue = this.GetNextTimeSpanValue(false);
                TimeZoneInfo.TransitionTime nextTransitionTimeValue = this.GetNextTransitionTimeValue(false);
                TimeZoneInfo.TransitionTime daylightTransitionEnd = this.GetNextTransitionTimeValue(false);
                if ((this.m_state == State.EndOfLine) || (this.m_currentTokenStartIndex >= this.m_serializedText.Length))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                if (this.m_serializedText[this.m_currentTokenStartIndex] != ']')
                {
                    this.SkipVersionNextDataFields(1);
                }
                else
                {
                    this.m_currentTokenStartIndex++;
                }
                try
                {
                    rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(nextDateTimeValue, dateEnd, nextTimeSpanValue, nextTransitionTimeValue, daylightTransitionEnd);
                }
                catch
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"), exception);
                }
                if (this.m_currentTokenStartIndex >= this.m_serializedText.Length)
                {
                    this.m_state = State.EndOfLine;
                    return rule;
                }
                this.m_state = State.StartOfToken;
                return rule;
            }

            private DateTime GetNextDateTimeValue(bool canEndWithoutSeparator, string format)
            {
                DateTime time;
                if (!DateTime.TryParseExact(this.GetNextStringValue(canEndWithoutSeparator), format, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.None, out time))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                return time;
            }

            private int GetNextInt32Value(bool canEndWithoutSeparator)
            {
                int num;
                if (!int.TryParse(this.GetNextStringValue(canEndWithoutSeparator), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out num))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                return num;
            }

            private string GetNextStringValue(bool canEndWithoutSeparator)
            {
                if (this.m_state == State.EndOfLine)
                {
                    if (!canEndWithoutSeparator)
                    {
                        // //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                    }
                    return null;
                }
                if ((this.m_currentTokenStartIndex < 0) || (this.m_currentTokenStartIndex >= this.m_serializedText.Length))
                {
                    // //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                State notEscaped = State.NotEscaped;
                StringBuilder builder = new StringBuilder(0x40);
                for (int i = this.m_currentTokenStartIndex; i < this.m_serializedText.Length; i++)
                {
                    if (notEscaped == State.Escaped)
                    {
                        VerifyIsEscapableCharacter(this.m_serializedText[i]);
                        builder.Append(this.m_serializedText[i]);
                        notEscaped = State.NotEscaped;
                    }
                    else if (notEscaped == State.NotEscaped)
                    {
                        switch (this.m_serializedText[i])
                        {
                            case '[':
                            ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));

                            case '\\':
                                notEscaped = State.Escaped;
                                break;

                            case ']':
                                if (!canEndWithoutSeparator)
                                {
                                    // //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                                }
                                this.m_currentTokenStartIndex = i;
                                this.m_state = State.StartOfToken;
                                return builder.ToString();

                            case ';':
                                this.m_currentTokenStartIndex = i + 1;
                                if (this.m_currentTokenStartIndex >= this.m_serializedText.Length)
                                {
                                    this.m_state = State.EndOfLine;
                                }
                                else
                                {
                                    this.m_state = State.StartOfToken;
                                }
                                return builder.ToString();

                            case '\0':
                            // //throw new SerializationException(SR.GetString("Serialization_InvalidData"));

                            default:
                                builder.Append(this.m_serializedText[i]);
                                break;
                        }
                    }
                }
                if (notEscaped == State.Escaped)
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidEscapeSequence", new object[] { string.Empty }));
                }
                if (!canEndWithoutSeparator)
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                this.m_currentTokenStartIndex = this.m_serializedText.Length;
                this.m_state = State.EndOfLine;
                return builder.ToString();
            }

            private TimeSpan GetNextTimeSpanValue(bool canEndWithoutSeparator)
            {
                TimeSpan span;
                int minutes = this.GetNextInt32Value(canEndWithoutSeparator);
        
                    span = new TimeSpan(0, minutes, 0);
        
                return span;
            }

            private TimeZoneInfo.TransitionTime GetNextTransitionTimeValue(bool canEndWithoutSeparator)
            {
                TimeZoneInfo.TransitionTime time;
                if ((this.m_state == State.EndOfLine) || ((this.m_currentTokenStartIndex < this.m_serializedText.Length) && (this.m_serializedText[this.m_currentTokenStartIndex] == ']')))
                {
                    // //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                if ((this.m_currentTokenStartIndex < 0) || (this.m_currentTokenStartIndex >= this.m_serializedText.Length))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                if (this.m_serializedText[this.m_currentTokenStartIndex] != '[')
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                this.m_currentTokenStartIndex++;
                int num = this.GetNextInt32Value(false);
                if ((num != 0) && (num != 1))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                DateTime nextDateTimeValue = this.GetNextDateTimeValue(false, "HH:mm:ss.FFF");
                nextDateTimeValue = new DateTime(1, 1, 1, nextDateTimeValue.Hour, nextDateTimeValue.Minute, nextDateTimeValue.Second, nextDateTimeValue.Millisecond);
                int month = this.GetNextInt32Value(false);
                if (num == 1)
                {
                    int day = this.GetNextInt32Value(false);
                    try
                    {
                        time = TimeZoneInfo.TransitionTime.CreateFixedDateRule(nextDateTimeValue, month, day);
                        goto Label_015B;
                    }
                    catch
                    {
                        ////throw new SerializationException(SR.GetString("Serialization_InvalidData"), exception);
                    }
                }
                int week = this.GetNextInt32Value(false);
                int num5 = this.GetNextInt32Value(false);
                
                    time = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(nextDateTimeValue, month, week, (DayOfWeek)num5);
                
            Label_015B:
                if ((this.m_state == State.EndOfLine) || (this.m_currentTokenStartIndex >= this.m_serializedText.Length))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                if (this.m_serializedText[this.m_currentTokenStartIndex] != ']')
                {
                    this.SkipVersionNextDataFields(1);
                }
                else
                {
                    this.m_currentTokenStartIndex++;
                }
                bool flag = false;
                if ((this.m_currentTokenStartIndex < this.m_serializedText.Length) && (this.m_serializedText[this.m_currentTokenStartIndex] == ';'))
                {
                    this.m_currentTokenStartIndex++;
                    flag = true;
                }
                if (!flag && !canEndWithoutSeparator)
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                if (this.m_currentTokenStartIndex >= this.m_serializedText.Length)
                {
                    this.m_state = State.EndOfLine;
                    return time;
                }
                this.m_state = State.StartOfToken;
                return time;
            }

            public static string GetSerializedString(TimeZoneInfo zone)
            {
                StringBuilder serializedText = new StringBuilder();
                serializedText.Append(SerializeSubstitute(zone.Id));
                serializedText.Append(';');
                serializedText.Append(SerializeSubstitute(zone.BaseUtcOffset.TotalMinutes.ToString(CultureInfo.InvariantCulture)));
                serializedText.Append(';');
                serializedText.Append(SerializeSubstitute(zone.DisplayName));
                serializedText.Append(';');
                serializedText.Append(SerializeSubstitute(zone.StandardName));
                serializedText.Append(';');
                serializedText.Append(SerializeSubstitute(zone.DaylightName));
                serializedText.Append(';');
                TimeZoneInfo.AdjustmentRule[] adjustmentRules = zone.GetAdjustmentRules();
                if ((adjustmentRules != null) && (adjustmentRules.Length > 0))
                {
                    for (int i = 0; i < adjustmentRules.Length; i++)
                    {
                        TimeZoneInfo.AdjustmentRule rule = adjustmentRules[i];
                        serializedText.Append('[');
                        serializedText.Append(SerializeSubstitute(rule.DateStart.ToString("MM:dd:yyyy", DateTimeFormatInfo.InvariantInfo)));
                        serializedText.Append(';');
                        serializedText.Append(SerializeSubstitute(rule.DateEnd.ToString("MM:dd:yyyy", DateTimeFormatInfo.InvariantInfo)));
                        serializedText.Append(';');
                        serializedText.Append(SerializeSubstitute(rule.DaylightDelta.TotalMinutes.ToString(CultureInfo.InvariantCulture)));
                        serializedText.Append(';');
                        SerializeTransitionTime(rule.DaylightTransitionStart, serializedText);
                        serializedText.Append(';');
                        SerializeTransitionTime(rule.DaylightTransitionEnd, serializedText);
                        serializedText.Append(';');
                        serializedText.Append(']');
                    }
                }
                serializedText.Append(';');
                return serializedText.ToString();
            }

            private static string SerializeSubstitute(string text)
            {
                text = text.Replace(@"\", @"\\");
                text = text.Replace("[", @"\[");
                text = text.Replace("]", @"\]");
                return text.Replace(";", @"\;");
            }

            private static void SerializeTransitionTime(TimeZoneInfo.TransitionTime time, StringBuilder serializedText)
            {
                serializedText.Append('[');
                serializedText.Append((time.IsFixedDateRule ? 1 : 0).ToString(CultureInfo.InvariantCulture));
                serializedText.Append(';');
                if (time.IsFixedDateRule)
                {
                    serializedText.Append(SerializeSubstitute(time.TimeOfDay.ToString("HH:mm:ss.FFF", DateTimeFormatInfo.InvariantInfo)));
                    serializedText.Append(';');
                    serializedText.Append(SerializeSubstitute(time.Month.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(';');
                    serializedText.Append(SerializeSubstitute(time.Day.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(';');
                }
                else
                {
                    serializedText.Append(SerializeSubstitute(time.TimeOfDay.ToString("HH:mm:ss.FFF", DateTimeFormatInfo.InvariantInfo)));
                    serializedText.Append(';');
                    serializedText.Append(SerializeSubstitute(time.Month.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(';');
                    serializedText.Append(SerializeSubstitute(time.Week.ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(';');
                    serializedText.Append(SerializeSubstitute(((int)time.DayOfWeek).ToString(CultureInfo.InvariantCulture)));
                    serializedText.Append(';');
                }
                serializedText.Append(']');
            }

            private void SkipVersionNextDataFields(int depth)
            {
                if ((this.m_currentTokenStartIndex < 0) || (this.m_currentTokenStartIndex >= this.m_serializedText.Length))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                }
                State notEscaped = State.NotEscaped;
                for (int i = this.m_currentTokenStartIndex; i < this.m_serializedText.Length; i++)
                {
                    switch (notEscaped)
                    {
                        case State.Escaped:
                            {
                                VerifyIsEscapableCharacter(this.m_serializedText[i]);
                                notEscaped = State.NotEscaped;
                                continue;
                            }
                        case State.NotEscaped:
                            switch (this.m_serializedText[i])
                            {
                                case '[':
                                    {
                                        depth++;
                                        continue;
                                    }
                                case '\\':
                                    {
                                        notEscaped = State.Escaped;
                                        continue;
                                    }
                                case ']':
                                    depth--;
                                    if (depth != 0)
                                    {
                                        continue;
                                    }
                                    this.m_currentTokenStartIndex = i + 1;
                                    if (this.m_currentTokenStartIndex < this.m_serializedText.Length)
                                    {
                                        goto Label_00B5;
                                    }
                                    this.m_state = State.EndOfLine;
                                    return;

                                case '\0':
                                ////throw new SerializationException(SR.GetString("Serialization_InvalidData"));
                                break;
                            }
                            break;
                    }
                    continue;
                Label_00B5:
                    this.m_state = State.StartOfToken;
                    return;
                }
                //throw new SerializationException(SR.GetString("Serialization_InvalidData"));
            }

            private static void VerifyIsEscapableCharacter(char c)
            {
                if (((c != '\\') && (c != ';')) && ((c != '[') && (c != ']')))
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidEscapeSequence", new object[] { c }));
                }
            }

            // Nested Types
            private enum State
            {
                Escaped,
                NotEscaped,
                StartOfToken,
                EndOfLine
            }
        }

        private class TimeZoneInfoComparer : IComparer<TimeZoneInfo>
        {
            // Methods
            int IComparer<TimeZoneInfo>.Compare(TimeZoneInfo x, TimeZoneInfo y)
            {
                return string.Compare(x.DisplayName, y.DisplayName, StringComparison.Ordinal);
            }
        }

        private enum TimeZoneInfoResult
        {
            Success,
            TimeZoneNotFoundException,
            InvalidTimeZoneException,
            SecurityException
        }


        public struct TransitionTime : IEquatable<TimeZoneInfo.TransitionTime>
        {
            private DateTime m_timeOfDay;
            private byte m_month;
            private byte m_week;
            private byte m_day;
            private DayOfWeek m_dayOfWeek;
            private bool m_isFixedDateRule;
            public DateTime TimeOfDay
            {
                get
                {
                    return this.m_timeOfDay;
                }
            }
            public int Month
            {
                get
                {
                    return this.m_month;
                }
            }
            public int Week
            {
                get
                {
                    return this.m_week;
                }
            }
            public int Day
            {
                get
                {
                    return this.m_day;
                }
            }
            public DayOfWeek DayOfWeek
            {
                get
                {
                    return this.m_dayOfWeek;
                }
            }
            public bool IsFixedDateRule
            {
                get
                {
                    return this.m_isFixedDateRule;
                }
            }
            public override bool Equals(object obj)
            {
                return ((obj is TimeZoneInfo.TransitionTime) && this.Equals((TimeZoneInfo.TransitionTime)obj));
            }

            public static bool operator ==(TimeZoneInfo.TransitionTime left, TimeZoneInfo.TransitionTime right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(TimeZoneInfo.TransitionTime left, TimeZoneInfo.TransitionTime right)
            {
                return !left.Equals(right);
            }

            public bool Equals(TimeZoneInfo.TransitionTime other)
            {
                bool flag = ((this.m_isFixedDateRule == other.m_isFixedDateRule) && (this.m_timeOfDay == other.m_timeOfDay)) && (this.m_month == other.m_month);
                if (!flag)
                {
                    return flag;
                }
                if (other.m_isFixedDateRule)
                {
                    return (this.m_day == other.m_day);
                }
                return ((this.m_week == other.m_week) && (this.m_dayOfWeek == other.m_dayOfWeek));
            }

            public override int GetHashCode()
            {
                return (this.m_month ^ (this.m_week << 8));
            }

            public static TimeZoneInfo.TransitionTime CreateFixedDateRule(DateTime timeOfDay, int month, int day)
            {
                return CreateTransitionTime(timeOfDay, month, 1, day, DayOfWeek.Sunday, true);
            }

            public static TimeZoneInfo.TransitionTime CreateFloatingDateRule(DateTime timeOfDay, int month, int week, DayOfWeek dayOfWeek)
            {
                return CreateTransitionTime(timeOfDay, month, week, 1, dayOfWeek, false);
            }

            private static TimeZoneInfo.TransitionTime CreateTransitionTime(DateTime timeOfDay, int month, int week, int day, DayOfWeek dayOfWeek, bool isFixedDateRule)
            {
                ValidateTransitionTime(timeOfDay, month, week, day, dayOfWeek);
                return new TimeZoneInfo.TransitionTime { m_isFixedDateRule = isFixedDateRule, m_timeOfDay = timeOfDay, m_dayOfWeek = dayOfWeek, m_day = (byte)day, m_week = (byte)week, m_month = (byte)month };
            }

            private static void ValidateTransitionTime(DateTime timeOfDay, int month, int week, int day, DayOfWeek dayOfWeek)
            {
                if (timeOfDay.Kind != DateTimeKind.Unspecified)
                {
                    //throw new ArgumentException(SR.GetString("Argument_DateTimeKindMustBeUnspecified"), "timeOfDay");
                }
                if ((month < 1) || (month > 12))
                {
                    //throw new ArgumentOutOfRangeException("month", SR.GetString("ArgumentOutOfRange_Month"));
                }
                if ((day < 1) || (day > 0x1f))
                {
                    //throw new ArgumentOutOfRangeException("day", SR.GetString("ArgumentOutOfRange_Day"));
                }
                if ((week < 1) || (week > 5))
                {
                    //throw new ArgumentOutOfRangeException("week", SR.GetString("ArgumentOutOfRange_Week"));
                }
                if ((dayOfWeek < DayOfWeek.Sunday) || (dayOfWeek > DayOfWeek.Saturday))
                {
                    //throw new ArgumentOutOfRangeException("dayOfWeek", SR.GetString("ArgumentOutOfRange_DayOfWeek"));
                }
                if (((timeOfDay.Year != 1) || (timeOfDay.Month != 1)) || ((timeOfDay.Day != 1) || ((timeOfDay.Ticks % 0x2710L) != 0L)))
                {
                    //throw new ArgumentException(SR.GetString("Argument_DateTimeHasTicks"), "timeOfDay");
                }
            }

            void OnDeserialization(object sender)
            {
                try
                {
                    ValidateTransitionTime(this.m_timeOfDay, this.m_month, this.m_week, this.m_day, this.m_dayOfWeek);
                }
                catch
                {
                    ////throw new SerializationException(SR.GetString("Serialization_InvalidData"), exception);
                }
            }



        }

        public void SetLocalTimeZoneBySerializedString(string localTimeZoneString)
        {
            s_localTimeZone = FromSerializedString(localTimeZoneString);
        }

    }


    internal enum TimeZoneInfoOptions
    {
        None = 1,
        NoThrowOnInvalidTime = 2
    }




    internal class DaylightTime
    {
        // Fields
        internal TimeSpan m_delta;
        internal DateTime m_end;
        internal DateTime m_start;

        // Methods
        private DaylightTime()
        {
        }

        public DaylightTime(DateTime start, DateTime end, TimeSpan delta)
        {
            this.m_start = start;
            this.m_end = end;
            this.m_delta = delta;
        }

        // Properties
        public TimeSpan Delta
        {
            get
            {
                return this.m_delta;
            }
        }

        public DateTime End
        {
            get
            {
                return this.m_end;
            }
        }

        public DateTime Start
        {
            get
            {
                return this.m_start;
            }
        }
    }


}
