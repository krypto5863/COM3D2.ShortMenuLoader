using System;
using UnityEngine;

namespace ShortMenuLoader
{
	internal class TimedWaitUntil : CustomYieldInstruction
	{
		private float _nextCheck;
		private readonly float _checkInterval;
		private readonly Func<bool> _func;

		public TimedWaitUntil(Func<bool> predicate, float time)
		{
			_func = predicate;
			_checkInterval = time;
			_nextCheck = Time.realtimeSinceStartup + _checkInterval;
		}

		public override bool keepWaiting
		{
			get
			{
				if (!(Time.realtimeSinceStartup > _nextCheck))
				{
					return true;
				}

				if (_func())
				{
					return false;
				}

				_nextCheck = Time.realtimeSinceStartup + _checkInterval;
				return true;
			}
		}
	}
}