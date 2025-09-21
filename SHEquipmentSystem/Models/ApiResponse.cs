// 文件路径: src/DiceEquipmentSystem/Models/ApiResponse.cs
using DiceEquipmentSystem.Data.Repositories;
using System;
using System.Collections.Generic;

namespace DiceEquipmentSystem.Models
{
    /// <summary>
    /// 无数据的API响应结果
    /// </summary>
    public class ApiResponse
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static ApiResponse CreateSuccess(string message = "操作成功")
        {
            return new ApiResponse
            {
                Success = true,
                Message = message,
                Errors = new List<string>(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建失败响应
        /// </summary>
        public static ApiResponse CreateFailure(string message, List<string>? errors = null)
        {
            return new ApiResponse
            {
                Success = false,
                Message = message,
                Errors = errors ?? new List<string>(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建失败响应（单个错误）
        /// </summary>
        public static ApiResponse CreateFailure(string message, string error)
        {
            return new ApiResponse
            {
                Success = false,
                Message = message,
                Errors = new List<string> { error },
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 泛型API响应结果
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 响应数据
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static ApiResponse<T> CreateSuccess(T data, string message = "操作成功")
        {
            return new ApiResponse<T>
            {
                Success = true,
                Message = message,
                Data = data,
                Errors = new List<string>(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建失败响应
        /// </summary>
        public static ApiResponse<T> CreateFailure(string message, List<string>? errors = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Data = default(T),
                Errors = errors ?? new List<string>(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建失败响应（单个错误）
        /// </summary>
        public static ApiResponse<T> CreateFailure(string message, string error)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Data = default(T),
                Errors = new List<string> { error },
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 分页响应结果
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class PagedApiResponse<T>
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 分页数据
        /// </summary>
        public PagedResult<T>? Data { get; set; }

        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static PagedApiResponse<T> CreateSuccess(PagedResult<T> data, string message = "查询成功")
        {
            return new PagedApiResponse<T>
            {
                Success = true,
                Message = message,
                Data = data,
                Errors = new List<string>(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建失败响应
        /// </summary>
        public static PagedApiResponse<T> CreateFailure(string message, List<string>? errors = null)
        {
            return new PagedApiResponse<T>
            {
                Success = false,
                Message = message,
                Data = null,
                Errors = errors ?? new List<string>(),
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 验证响应结果
    /// </summary>
    public class ValidationResponse
    {
        /// <summary>
        /// 验证是否通过
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 验证错误详情（字段名 -> 错误列表）
        /// </summary>
        public Dictionary<string, List<string>> ValidationErrors { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// 所有错误的扁平列表
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建验证成功响应
        /// </summary>
        public static ValidationResponse CreateSuccess(string message = "验证通过")
        {
            return new ValidationResponse
            {
                Success = true,
                Message = message,
                ValidationErrors = new Dictionary<string, List<string>>(),
                Errors = new List<string>(),
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建验证失败响应
        /// </summary>
        public static ValidationResponse CreateFailure(Dictionary<string, List<string>> validationErrors, string message = "验证失败")
        {
            var allErrors = new List<string>();
            foreach (var kvp in validationErrors)
            {
                allErrors.AddRange(kvp.Value);
            }

            return new ValidationResponse
            {
                Success = false,
                Message = message,
                ValidationErrors = validationErrors,
                Errors = allErrors,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建验证失败响应（单个字段错误）
        /// </summary>
        public static ValidationResponse CreateFailure(string field, string error, string message = "验证失败")
        {
            var validationErrors = new Dictionary<string, List<string>>
            {
                { field, new List<string> { error } }
            };

            return CreateFailure(validationErrors, message);
        }

        /// <summary>
        /// 创建验证失败响应（单个错误）
        /// </summary>
        public static ValidationResponse CreateFailure(string message, string error)
        {
            return new ValidationResponse
            {
                Success = false,
                Message = message,
                ValidationErrors = new Dictionary<string, List<string>>(),
                Errors = new List<string> { error },
                Timestamp = DateTime.UtcNow
            };
        }
    }
}