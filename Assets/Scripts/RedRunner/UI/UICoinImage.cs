using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using RedRunner.Collectables;

namespace RedRunner.UI
{

	public class UICoinImage : Image
	{

		[SerializeField]
		protected ParticleSystem m_ParticleSystem;

		protected override void Awake ()
		{
			base.Awake ();
		}

		protected override void Start()
		{
			var gm = GameManager.Singleton;

			gm.m_Coin.AddEventAndFire(Coin_OnCoinCollected, this);
		}

		void Coin_OnCoinCollected (int coinValue)
		{
			var animator = GetComponent<Animator> ();
			if (animator != null)
			{
				animator.SetTrigger ("Collect");
			}
		}

		public virtual void PlayParticleSystem ()
		{
			m_ParticleSystem.Play ();
		}
	}
}
