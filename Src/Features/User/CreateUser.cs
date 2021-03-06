﻿using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MediatR;
using AutoMapper;
using DataModel;
using Hangfire;
using System.Data;

namespace Src.Features.User
{
    public class CreateUser
    {
        public class Command : IRequest<Result>
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string Email { get; set; }
        }

        public class Result
        {
            public Guid Id { get; set; }
        }

        public class GetUserValidator : AbstractValidator<Command>
        {
            public GetUserValidator()
            {
                RuleFor(user => user.FirstName).NotEmpty();
                RuleFor(user => user.LastName).NotEmpty();
                RuleFor(user => user.Email).NotEmpty().EmailAddress();
            }
        }

        public class MappingProfile : Profile
        {
            public MappingProfile()
            {
                CreateMap<Command, DataModel.Models.User>(MemberList.Source);
            }
        }

        public class CreateUserHandler : IRequestHandler<Command, Result>
        {
            private readonly DatabaseContext _db;
            private readonly IMapper _mapper;
            private readonly IBackgroundJobClient _jobClient;
            private readonly IMediator _mediator;

            public CreateUserHandler(
                DatabaseContext db, 
                IMapper mapper,
                IBackgroundJobClient jobClient,
                IMediator mediator)
            {
                _db = db;
                _mapper = mapper;
                _jobClient = jobClient;
                _mediator = mediator;
            }

            public async Task<Result> Handle(Command message, CancellationToken cancellationToken)
            {
                var userExists = await _mediator.Send(new DoesUserExist.Query(message.Email));

                if (userExists)
                {
                    throw new DuplicateNameException($"{nameof(message.Email)} already exists");
                }

                var user = _mapper.Map<Command, DataModel.Models.User>(message);

                _jobClient.Enqueue(() => CreateUser(user));

                await Task.Run(() => _jobClient.Enqueue(() => CreateUser(user))).ConfigureAwait(false);

                var result = new Result
                {
                    Id = user.Id
                };

                return result;
            }

            public void CreateUser(DataModel.Models.User user)
            {
                _db.Users.Add(user);

                _db.SaveChanges();
            }
        }
    }
}
