﻿using Castle.DynamicProxy;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Colder.CommonUtil
{
    /// <summary>
    /// 自动注入服务扩展
    /// </summary>
    public static class DependencyExtentions
    {
        private static readonly ProxyGenerator _generator = new ProxyGenerator();

        /// <summary>
        /// 自动注入服务
        /// 服务必须继承ITransientDependency、IScopedDependency或ISingletonDependency
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            Dictionary<Type, ServiceLifetime> lifeTimeMap = new Dictionary<Type, ServiceLifetime>
            {
                { typeof(ITransientDependency), ServiceLifetime.Transient},
                { typeof(IScopedDependency),ServiceLifetime.Scoped},
                { typeof(ISingletonDependency),ServiceLifetime.Singleton}
            };

            AssemblyHelper.AllTypes.ToList().ForEach(aImplementType =>
            {
                lifeTimeMap.ToList().ForEach(aMap =>
                {
                    var theDependency = aMap.Key;
                    if (theDependency.IsAssignableFrom(aImplementType) && theDependency != aImplementType && !aImplementType.IsAbstract && aImplementType.IsClass)
                    {
                        var interfaces = AssemblyHelper.AllTypes.Where(x => x.IsAssignableFrom(aImplementType) && x.IsInterface && x != theDependency).ToList();
                        //有接口则注入接口
                        if (interfaces.Count > 0)
                        {
                            interfaces.ForEach(aInterface =>
                            {
                                //注入AOP
                                services.Add(new ServiceDescriptor(aInterface, serviceProvider =>
                                {
                                    CastleInterceptor castleInterceptor = new CastleInterceptor(serviceProvider);

                                    return _generator.CreateInterfaceProxyWithTarget(
                                        aInterface,
                                        ActivatorUtilities.CreateInstance(serviceProvider, aImplementType),
                                        castleInterceptor);
                                }, aMap.Value));
                            });
                        }
                        //无接口直接注入自己
                        else
                        {
                            services.Add(new ServiceDescriptor(aImplementType, aImplementType, aMap.Value));
                        }
                    }
                });
            });

            return services;
        }
    }
}
