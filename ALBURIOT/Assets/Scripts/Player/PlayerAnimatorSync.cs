using Photon.Pun;
using UnityEngine;

// syncs essential animator params over the network so remote players see correct animations
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class PlayerAnimatorSync : MonoBehaviourPun, IPunObservable
{
    private Animator _anim;
    private CharacterController _cc;
    private ThirdPersonController _tpc;
    private PlayerCombat _combat;

    // local-only derived values
    private int _attackSeq = 0;
    private bool _prevIsAttacking = false;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        _cc = GetComponent<CharacterController>();
        _tpc = GetComponent<ThirdPersonController>();
        _combat = GetComponent<PlayerCombat>();
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            bool nowAttacking = _combat != null && _combat.IsAttacking;
            if (nowAttacking && !_prevIsAttacking)
            {
                // rising edge: increment sequence so remotes can set trigger exactly once
                _attackSeq++;
            }
            _prevIsAttacking = nowAttacking;
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // collect local animator state to send
            float speed = 0f;
            if (_cc != null)
            {
                var v = _cc.velocity;
                speed = new Vector3(v.x, 0f, v.z).magnitude;
            }
            bool isWalking = _anim != null && _anim.GetBool("IsWalking");
            bool isRunning = _anim != null && _anim.GetBool("IsRunning");
            bool isJumping = _anim != null && _anim.GetBool("IsJumping");
            bool isCrouched = _anim != null && _anim.GetBool("IsCrouched");
            bool isRolling = false;
            if (_tpc != null) isRolling = _tpc.IsRolling;

            stream.SendNext(speed);
            stream.SendNext(isWalking);
            stream.SendNext(isRunning);
            stream.SendNext(isJumping);
            stream.SendNext(isCrouched);
            stream.SendNext(isRolling);
            stream.SendNext(_attackSeq);
        }
        else
        {
            // apply received state to remote animator
            float speed = (float)stream.ReceiveNext();
            bool isWalking = (bool)stream.ReceiveNext();
            bool isRunning = (bool)stream.ReceiveNext();
            bool isJumping = (bool)stream.ReceiveNext();
            bool isCrouched = (bool)stream.ReceiveNext();
            bool isRolling = (bool)stream.ReceiveNext();
            int attackSeqRemote = (int)stream.ReceiveNext();

            if (_anim != null)
            {
                _anim.SetFloat("Speed", speed);
                _anim.SetBool("IsWalking", isWalking);
                _anim.SetBool("IsRunning", isRunning);
                _anim.SetBool("IsJumping", isJumping);
                _anim.SetBool("IsCrouched", isCrouched);
                if (AnimatorHasParameter(_anim, "IsRolling"))
                    _anim.SetBool("IsRolling", isRolling);
            }

            // detect new attacks and play the trigger on remote
            if (attackSeqRemote != _lastRecvAttackSeq)
            {
                _lastRecvAttackSeq = attackSeqRemote;
                if (_anim != null && AnimatorHasParameter(_anim, "Attack"))
                {
                    _anim.SetTrigger("Attack");
                }
            }
        }
    }

    private int _lastRecvAttackSeq = 0;

    private static bool AnimatorHasParameter(Animator anim, string paramName)
    {
        if (anim == null) return false;
        foreach (var p in anim.parameters)
        {
            if (p.name == paramName) return true;
        }
        return false;
    }
}
