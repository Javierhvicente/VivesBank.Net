﻿using Newtonsoft.Json;
using StackExchange.Redis;
using VivesBankApi.Rest.Users.Dtos;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Mapper;
using VivesBankApi.Rest.Users.Models;
using VivesBankApi.Rest.Users.Repository;
using VivesBankApi.Rest.Users.Validator;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace VivesBankApi.Rest.Users.Service;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IDatabase _cache;
    
    public UserService(IUserRepository userRepository, IConnectionMultiplexer connectionMultiplexer)
    {
        _userRepository = userRepository;
        _cache = connectionMultiplexer.GetDatabase();
    }
    
    public async Task<PagedList<UserResponse>> GetAllUsersAsync(
        int pageNumber, 
        int pageSize,
        string role,
        bool? isDeleted,
        string direction = "asc")
    {
        var users = await _userRepository.GetAllUsersPagedAsync(pageNumber, pageSize, role, isDeleted, direction);
        var mappedUsers = new PagedList<UserResponse>(
            users.Select(u => u.ToUserResponse()),
            users.TotalCount,
            users.PageNumber,
            users.PageSize
        );
        return mappedUsers;
    }
    
     public async Task<UserResponse> GetUserByIdAsync(string id)
    {
       var user = await GetByIdAsync(id) ?? throw new UserNotFoundException(id);
       return user.ToUserResponse();
    }

    public async Task<UserResponse> AddUserAsync(CreateUserRequest userRequest)
    {
        if (!UserValidator.ValidateDni(userRequest.Username))
        {
            throw new  InvalidUsernameException(userRequest.Username);
        }
        User newUser = userRequest.ToUser();
        User? userWithTheSameUsername = await GetByUsernameAsync(userRequest.Username);
        if (userWithTheSameUsername != null)
        {
            throw new UserAlreadyExistsException(userRequest.Username);
        }
        await _userRepository.AddAsync(newUser);
        return newUser.ToUserResponse();
    }

    public async Task<UserResponse> GetUserByUsernameAsync(string username)
    {
        var user = await GetByUsernameAsync(username) ?? throw new UserNotFoundException(username);
        return user.ToUserResponse();
    }


    public async Task<UserResponse> UpdateUserAsync(String id, UserUpdateRequest user)
    {
        if (user.Username != null && !UserValidator.ValidateDni(user.Username))
        {
             throw new InvalidUsernameException(user.Username);
        }

        User? userToUpdate = await GetByIdAsync(id) ?? throw new UserNotFoundException(id);
        
        if (user.Username != null)
        {
            User? userWithTheSameUsername = await GetByUsernameAsync(userToUpdate.Username);
            if (userWithTheSameUsername != null && userWithTheSameUsername.Id != id)
            {
                throw new UserAlreadyExistsException(user.Username);
            }
        }
        
        User updatedUser = user.UpdateUserFromInput(userToUpdate);
        await _userRepository.UpdateAsync(updatedUser);
        // Removing old cache entry
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("users:" + userToUpdate.Username.Trim().ToUpper());
        // Adding new cache entry
        await _cache.StringSetAsync(id, JsonConvert.SerializeObject(updatedUser), TimeSpan.FromMinutes(10));
        return updatedUser.ToUserResponse();
    }

    public async Task DeleteUserAsync(String id, bool logically)
    {
        User? userToUpdate = await _userRepository.GetByIdAsync(id);
        if (userToUpdate == null)
        {
            throw new UserNotFoundException(id);
        }
        
        if (logically)
        {
            userToUpdate.IsDeleted = true;
            await _userRepository.UpdateAsync(userToUpdate);
            await _cache.KeyDeleteAsync(id);
            await _cache.KeyDeleteAsync("users:" + userToUpdate.Username.Trim().ToUpper());
        }
        else
        {
            await _cache.KeyDeleteAsync(id);
            await _cache.KeyDeleteAsync("users:" + userToUpdate.Username.Trim().ToUpper());
            await _userRepository.DeleteAsync(id);
        }
    }

    private async Task<User?> GetByIdAsync(string id)
    {
        // Try to get from cache first
        var cachedUser = await _cache.StringGetAsync(id);
        if (!cachedUser.IsNullOrEmpty)
        {
            var json = await _cache.StringGetAsync(id);
            
            if (!json.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<User>(json);
            }
        }

        // If not in cache, get from DB and cache it
        User? user = await _userRepository.GetByIdAsync(id);
        if (user != null)
        {
            await _cache.StringSetAsync(id, JsonConvert.SerializeObject(user), TimeSpan.FromMinutes(10));
            return user;
        }
        return null;
    }

    private async Task<User?> GetByUsernameAsync(string username)
    {
        // Try to get from cache first
        var cachedUser = await _cache.StringGetAsync("users:" + username.Trim().ToUpper());
        if (!cachedUser.IsNullOrEmpty)
        {
            var json = await _cache.StringGetAsync(username);
            
            if (!json.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<User>(json);
            }
        }
        // If not in cache, get from DB and cache it
        User? user = await _userRepository.GetByUsernameAsync(username);
        if (user != null)
        {
            await _cache.StringSetAsync("users:" + username.Trim().ToUpper(), JsonConvert.SerializeObject(user), TimeSpan.FromMinutes(10));
            return user;
        }
        return null;
    }
}