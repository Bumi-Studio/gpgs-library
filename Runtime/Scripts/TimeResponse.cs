namespace Rto.Library
{
	[System.Serializable]
	public class TimeResponse
	{
		public int year;
		public int month;
		public int day;
		public int hour;
		public int minute;
		public int seconds;
		public int milliseconds;
		public string dateTime;
		public string date;
		public string time;
		public string timeZone;
		public string dayOfWeek;
		public bool dstActive;
	}
}
