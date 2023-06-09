using System;
using System.Collections;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.AI;
using UnityEngine.Events;

abstract class EnemyAnimation_Universal : MonoBehaviour
{
    protected EnemyScript _enemyScript;
    private NavMeshAgent _agent;
    private GameObject _rotatableChild;
    protected Animator _animator;

    private Vector2 _enemyRotation;
    float EnemyPosX = 0; float EnemyPosY = 0;

    private bool _aggroed; private bool _afraid;
    private bool _slowed; private bool _stunned;

    // 0 - idle; 1 - walk; 2 - aggro
    private float _state;
    private float _animationSpeedModifier;

    // Start is called before the first frame update
    void Start()
    {
        // Receive all elements necessary for direction monitoring
        _enemyScript = gameObject.GetComponent<EnemyScript>();
        _agent = gameObject.GetComponent<NavMeshAgent>();
        _rotatableChild = transform.Find("RotatableChild").gameObject;
        _animator = gameObject.GetComponent<Animator>();
    }

    public void MonitorEnemyDirection()
    {
        
        if (_agent.desiredVelocity.x != 0 || _agent.desiredVelocity.y != 0 && _enemyScript.CurrentTarget != gameObject.transform)
        {
            // only update enemy rotation data if speed > 0. Otherwise - keep last known rotation data
            EnemyPosX = (transform.position.x + _agent.desiredVelocity.x) - transform.position.x;
            EnemyPosY = (transform.position.y + _agent.desiredVelocity.y) - transform.position.y;
        }
        // Specifically for throwers in case they get aggroed with their back turned to the player
        if (_enemyScript.CurrentTarget == gameObject.transform)
        {
            if (_enemyScript._player != null)
            {
                // only update enemy rotation data if speed > 0. Otherwise - keep last known rotation data
                EnemyPosX = (_enemyScript._player.transform.position.x) - transform.position.x;
                EnemyPosY = (_enemyScript._player.transform.position.y) - transform.position.y;
            }
        }
        
        float angle = Mathf.Atan2(EnemyPosY, EnemyPosX) * Mathf.Rad2Deg;
        _rotatableChild.transform.rotation = Quaternion.Euler(new Vector3(0, 0, angle - 90));

        Vector2 direction = _rotatableChild.transform.up;

        _enemyRotation = direction;

    }

    public void MonitorEnemyState()
    {
        _aggroed = _enemyScript.CurrentlyAggroed; _afraid = _enemyScript.IsAfraid;
        _slowed = _enemyScript.Slowed; _stunned = _enemyScript.Stunned;
        bool onTopOfPlayer = false;

        // Make sure that if chaser is on top - he is idle, not running
        if (_enemyScript._player != null)
        {
            float dist = Vector3.Distance(new Vector3(gameObject.transform.position.x, gameObject.transform.position.y, 0), new Vector3(_enemyScript._player.transform.position.x, _enemyScript._player.transform.position.y, 0));
            if (
                dist <= 0.04f &&
                _enemyScript.EnemyType == EnemyScript.EnemyOfType.Roamer &&
                _enemyScript.EnemyLevel == 2
                ) 
            { onTopOfPlayer = true; }
            else onTopOfPlayer = false;
        }

        if 
            (
            _aggroed && !_afraid && 
            !_stunned && 
            !onTopOfPlayer
            ) 
        { _state = 2; }
        else if 
            ( 
            (_agent.desiredVelocity.x != 0 || _agent.desiredVelocity.y != 0) && 
            (!_enemyScript._onCooldown) && 
            !_stunned && !onTopOfPlayer 
            ) 
        { _state = 1; }
        else _state = 0;

        // if (_stunned) _animationSpeedModifier = 0f;
        if (_stunned || _slowed) _animationSpeedModifier = _enemyScript.SlowToSpeed;
        else _animationSpeedModifier = 1f;
    }

    public void PassInformationToAnimator()
    {
        _animator.SetFloat("Horizontal", _enemyRotation.x);
        _animator.SetFloat("Vertical", _enemyRotation.y);
        _animator.SetFloat("State", _state);
        _animator.speed = _animationSpeedModifier;
    }
}
