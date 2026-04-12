using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using RedRunner.Collectables;
using System;

namespace RedRunner.UI
{
	public class UICoinText : UIText
	{
		[SerializeField]
		protected string m_CoinTextFormat = "x {0}";

		protected override void Awake ()
		{
			base.Awake ();
		}

		protected override void Start()
		{
			var gm = GameManager.Singleton ?? FindFirstObjectByType<GameManager>();
			gm.m_Coin.AddEventAndFire(UpdateCoinsText, this);
		}

		private void UpdateCoinsText(int newCoinValue)
		{
			var animator = GetComponent<Animator>();
			if (animator != null)
			{
				animator.SetTrigger("Collect");
			}
			text = string.Format(m_CoinTextFormat, newCoinValue);
		}
	}
}
